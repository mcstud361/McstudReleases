#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Statistics View - Per-user stats with multiple visualization modes
    /// </summary>
    public sealed class StatisticsView : UserControl
    {
        private readonly ExportStatisticsService _exportStats;
        private readonly EstimateStatisticsService _estimateStats;

        // Header controls
        private ComboBox? _userCombo;
        private ComboBox? _periodCombo;
        private ComboBox? _viewTypeCombo;
        private Button? _clearStatsButton;

        // Content
        private Grid? _contentArea;
        private string _currentViewType = "all"; // all, exports, shopdocs, operations, charts
        private string _currentUserId = "";

        // Colors
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentGreen = Color.FromArgb(255, 0, 150, 80);
        private static readonly Color AccentPurple = Color.FromArgb(255, 180, 100, 255);
        private static readonly Color AccentOrange = Color.FromArgb(255, 255, 150, 0);
        private static readonly Color AccentRed = Color.FromArgb(255, 220, 80, 80);
        private static readonly Color AccentCyan = Color.FromArgb(255, 0, 180, 200);
        private static readonly Color DarkBg = Color.FromArgb(255, 18, 18, 18);
        private static readonly Color CardBg = Color.FromArgb(255, 28, 28, 28);
        private static readonly Color SectionBg = Color.FromArgb(255, 35, 35, 35);

        // Chart dimensions
        private const double ChartWidth = 600;
        private const double ChartHeight = 300;
        private const double BarMaxWidth = 500;
        private const double ColumnMaxHeight = 240;

        // 3D Heatmap interactive state
        private Canvas? _3dHeatmapCanvas;
        private Grid? _3dHeatmapContainer;
        private ScaleTransform? _3dScaleTransform;
        private TranslateTransform? _3dTranslateTransform;
        private double _3dZoomLevel = 1.0;
        private double _3dPanX = 0;
        private double _3dPanY = 0;
        private double _3dRotationAngle = 0; // 0-360 degrees
        private bool _3dIsDragging = false;
        private Windows.Foundation.Point _3dLastPointerPosition;
        private List<HourlyActivity>? _cached3DHourlyActivity;
        private TextBlock? _3dZoomLabel;

        // Chart selection state
        private string _selectedChartCategory = "bar";
        private string _selectedChart = "operations_bar";
        private ComboBox? _chartCategoryCombo;
        private ComboBox? _chartItemCombo;
        private StackPanel? _chartsContainer;

        // Interactive chart state
        private Grid? _interactiveChartContainer;
        private ScaleTransform? _chartScaleTransform;
        private TranslateTransform? _chartTranslateTransform;
        private double _chartZoomLevel = 1.0;
        private double _chartPanX = 0, _chartPanY = 0;
        private bool _chartIsDragging = false;
        private Windows.Foundation.Point _chartLastPointerPosition;
        private TextBlock? _chartZoomLabel;
        private bool _chartIsExpanded = true;
        private Border? _chartExpandContainer;
        private StackPanel? _chartSeriesPanel;
        private string _currentChartKey = "";
        private bool _isSeriesToggleRefresh = false;
        private bool _isRefreshingChart = false;
        private Dictionary<string, bool> _chartSeriesVisibility = new();

        public StatisticsView()
        {
            _exportStats = new ExportStatisticsService();
            _estimateStats = EstimateStatisticsService.Instance;
            _currentUserId = ExportStatisticsService.GetCurrentUserId();
            BuildUI();
            RefreshStats();
        }

        private void BuildUI()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(DarkBg),
                Padding = new Thickness(0)
            };

            // Use Grid instead of StackPanel so ScrollViewer gets constrained height
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // === HEADER WITH FILTERS ===
            var header = BuildHeader();
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // === CONTENT AREA ===
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12)
            };

            _contentArea = new Grid();
            scrollViewer.Content = _contentArea;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            mainBorder.Child = mainGrid;
            Content = mainBorder;
        }

        private Grid BuildHeader()
        {
            var headerGrid = new Grid
            {
                Background = new SolidColorBrush(CardBg),
                Padding = new Thickness(12, 10, 12, 10)
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 18,
                Foreground = new SolidColorBrush(AccentBlue)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Statistics",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titleStack, 0);

            // View Type selector
            _viewTypeCombo = new ComboBox
            {
                SelectedIndex = 0,
                MinWidth = 130,
                Background = new SolidColorBrush(SectionBg),
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0)
            };
            _viewTypeCombo.Items.Add(new ComboBoxItem { Content = "All Stats", Tag = "all" });
            _viewTypeCombo.Items.Add(new ComboBoxItem { Content = "Exports", Tag = "exports" });
            _viewTypeCombo.Items.Add(new ComboBoxItem { Content = "Shop Docs & Invoices", Tag = "shopdocs" });
            _viewTypeCombo.Items.Add(new ComboBoxItem { Content = "Operations", Tag = "operations" });
            _viewTypeCombo.Items.Add(new ComboBoxItem { Content = "Charts", Tag = "charts" });
            _viewTypeCombo.SelectionChanged += (s, e) =>
            {
                if (_viewTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string viewType)
                {
                    _currentViewType = viewType;
                    RefreshStats();
                }
            };
            Grid.SetColumn(_viewTypeCombo, 2);

            // User selector
            _userCombo = new ComboBox
            {
                MinWidth = 100,
                Background = new SolidColorBrush(SectionBg),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            };
            RefreshUserList();
            _userCombo.SelectionChanged += (s, e) =>
            {
                if (_userCombo.SelectedItem is ComboBoxItem item && item.Tag is string userId)
                {
                    _currentUserId = userId;
                    RefreshStats();
                }
            };
            Grid.SetColumn(_userCombo, 3);

            // Period selector
            _periodCombo = new ComboBox
            {
                SelectedIndex = 2, // Default to This Month
                MinWidth = 100,
                Background = new SolidColorBrush(SectionBg),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _periodCombo.Items.Add("Today");
            _periodCombo.Items.Add("This Week");
            _periodCombo.Items.Add("This Month");
            _periodCombo.Items.Add("This Year");
            _periodCombo.Items.Add("All Time");
            _periodCombo.SelectionChanged += (s, e) => RefreshStats();
            Grid.SetColumn(_periodCombo, 4);

            // Clear button
            _clearStatsButton = new Button
            {
                Content = "Clear All",
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 130, 130)),
                Margin = new Thickness(8, 0, 0, 0)
            };
            _clearStatsButton.Click += ClearStats_Click;
            Grid.SetColumn(_clearStatsButton, 5);

            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(_viewTypeCombo);
            headerGrid.Children.Add(_userCombo);
            headerGrid.Children.Add(_periodCombo);
            headerGrid.Children.Add(_clearStatsButton);

            return headerGrid;
        }

        private void RefreshUserList()
        {
            if (_userCombo == null) return;
            _userCombo.Items.Clear();

            var users = _exportStats.GetAllUserIds();
            var currentUser = ExportStatisticsService.GetCurrentUserId();

            // Always add current user first
            if (!users.Contains(currentUser))
                users.Insert(0, currentUser);

            foreach (var user in users)
            {
                var item = new ComboBoxItem
                {
                    Content = user == currentUser ? $"{user} (You)" : user,
                    Tag = user
                };
                _userCombo.Items.Add(item);

                if (user == _currentUserId)
                    _userCombo.SelectedItem = item;
            }

            // Select first if nothing selected
            if (_userCombo.SelectedItem == null && _userCombo.Items.Count > 0)
            {
                _userCombo.SelectedIndex = 0;
                if (_userCombo.SelectedItem is ComboBoxItem first && first.Tag is string userId)
                    _currentUserId = userId;
            }
        }

        private StatsPeriod GetSelectedPeriod()
        {
            return _periodCombo?.SelectedIndex switch
            {
                0 => StatsPeriod.Today,
                1 => StatsPeriod.ThisWeek,
                2 => StatsPeriod.ThisMonth,
                3 => StatsPeriod.ThisYear,
                4 => StatsPeriod.AllTime,
                _ => StatsPeriod.Today
            };
        }

        private void RefreshStats()
        {
            if (_contentArea == null) return;
            _contentArea.Children.Clear();

            var content = _currentViewType switch
            {
                "all" => BuildAllStatsView(),
                "exports" => BuildExportsView(),
                "shopdocs" => BuildShopDocsView(),
                "operations" => BuildOperationsTab(),
                "charts" => BuildChartsTab(),
                _ => BuildAllStatsView()
            };

            _contentArea.Children.Add(content);
        }

        #region Unified Stats Views

        private StackPanel BuildAllStatsView()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();

            // Summary header with key metrics
            stack.Children.Add(BuildSummaryHeader(period));

            // Export stats section
            stack.Children.Add(BuildExportStatsSection(period));

            // Business outcomes panel
            stack.Children.Add(BuildBusinessOutcomesPanel(period));

            // Shop docs section
            stack.Children.Add(CreateShopDocsStatsSection(period));

            // Top operations
            stack.Children.Add(BuildQuickOperationsSection(period));

            // Recent activity
            stack.Children.Add(BuildRecentActivitySection(period));

            return stack;
        }

        private StackPanel BuildExportsView()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();

            stack.Children.Add(BuildExportStatsSection(period));
            stack.Children.Add(BuildTopPartsSection(period));
            stack.Children.Add(BuildTopAddedOperationsSection(period));
            stack.Children.Add(BuildRecentActivitySection(period));

            return stack;
        }

        private Border BuildTopPartsSection(StatsPeriod period)
        {
            var partStats = _exportStats.GetPartTypeBreakdown(_currentUserId, period);

            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Top Parts", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });

            if (!partStats.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxCount = partStats.Max(p => p.Count);
                int barIndex = 0;
                foreach (var part in partStats.Take(8))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

                    row.Children.Add(new TextBlock { Text = part.PartType, FontSize = 11, Foreground = new SolidColorBrush(AccentBlue), VerticalAlignment = VerticalAlignment.Center });

                    var barWidth = maxCount > 0 ? (double)part.Count / maxCount * 160 : 0;
                    var bar = new Border { Width = 0, Height = 12, Background = new SolidColorBrush(AccentPurple), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
                    Grid.SetColumn(bar, 1); row.Children.Add(bar);
                    AnimateBarWidth(bar, barWidth, delayMs: barIndex * 60);

                    var cnt = new TextBlock { Text = $"{part.Percentage:F0}%", FontSize = 11, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cnt, 2); row.Children.Add(cnt);

                    stack.Children.Add(row);
                    barIndex++;
                }
            }

            border.Child = stack;
            return border;
        }

        private Border BuildTopAddedOperationsSection(StatsPeriod period)
        {
            var addedOps = _exportStats.GetTopAddedOperations(_currentUserId, period, 8);

            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Most Added Operations", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });

            if (!addedOps.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxCount = addedOps.Max(o => o.TimesAdded);
                int barIndex = 0;
                foreach (var op in addedOps)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

                    row.Children.Add(new TextBlock { Text = op.OperationType, FontSize = 11, Foreground = new SolidColorBrush(AccentOrange), VerticalAlignment = VerticalAlignment.Center });

                    var barWidth = maxCount > 0 ? (double)op.TimesAdded / maxCount * 160 : 0;
                    var bar = new Border { Width = 0, Height = 12, Background = new SolidColorBrush(AccentGreen), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
                    Grid.SetColumn(bar, 1); row.Children.Add(bar);
                    AnimateBarWidth(bar, barWidth, delayMs: barIndex * 60);

                    var cnt = new TextBlock { Text = op.TimesAdded.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cnt, 2); row.Children.Add(cnt);

                    stack.Children.Add(row);
                    barIndex++;
                }
            }

            border.Child = stack;
            return border;
        }

        private StackPanel BuildShopDocsView()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();
            var docTracker = DocumentUsageTrackingService.Instance;
            var summary = docTracker.GetUsageSummary(period, _currentUserId);
            var invoiceStats = docTracker.GetInvoiceStats(period, _currentUserId);
            var topDocs = docTracker.GetMostUsedDocuments(period, 10, _currentUserId);
            var productivity = docTracker.GetProductivityMetrics(period, _currentUserId);
            var recentActivity = docTracker.GetRecentActivity(20, _currentUserId);

            // Summary cards
            var summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var invoicesCard = CreateStatCard("TOTAL INVOICES", summary.TotalInvoices.ToString(), summary.FormattedInvoiceValue, AccentGreen, "\uE8C7", summary.TotalInvoices, "N0");
            var pdfsCard = CreateStatCard("PDFs EXPORTED", summary.TotalPdfExports.ToString(), $"{summary.TotalDocuments} docs", AccentBlue, "\uE8A5", summary.TotalPdfExports, "N0");
            var avgCard = CreateStatCard("AVG INVOICE", invoiceStats.FormattedAvg, $"{invoiceStats.AvgItems:F1} items", AccentOrange, "\uE8C8", (double)invoiceStats.AvgValue, "C0");

            Grid.SetColumn(invoicesCard, 0);
            Grid.SetColumn(pdfsCard, 2);
            Grid.SetColumn(avgCard, 4);
            summaryGrid.Children.Add(invoicesCard);
            summaryGrid.Children.Add(pdfsCard);
            summaryGrid.Children.Add(avgCard);
            stack.Children.Add(summaryGrid);

            // Invoice breakdown
            var invoiceSection = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var invoiceStack = new StackPanel { Spacing = 12 };
            invoiceStack.Children.Add(new TextBlock { Text = "Invoice Breakdown", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });

            var typesGrid = new Grid();
            typesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            typesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            typesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var colorTint = CreateInvoiceTypeMini("Color Tint", summary.ColorTintInvoices, invoiceStats.ByType.GetValueOrDefault("ColorTint")?.TotalValue ?? 0, Color.FromArgb(255, 100, 180, 255));
            var shopStock = CreateInvoiceTypeMini("Shop Stock", summary.ShopStockInvoices, invoiceStats.ByType.GetValueOrDefault("ShopStock")?.TotalValue ?? 0, Color.FromArgb(255, 100, 200, 100));
            var towBills = CreateInvoiceTypeMini("Tow Bills", summary.TowBills, invoiceStats.ByType.GetValueOrDefault("TowBill")?.TotalValue ?? 0, Color.FromArgb(255, 255, 180, 100));

            Grid.SetColumn(colorTint, 0);
            Grid.SetColumn(shopStock, 1);
            Grid.SetColumn(towBills, 2);
            typesGrid.Children.Add(colorTint);
            typesGrid.Children.Add(shopStock);
            typesGrid.Children.Add(towBills);
            invoiceStack.Children.Add(typesGrid);
            invoiceSection.Child = invoiceStack;
            stack.Children.Add(invoiceSection);

            // Productivity
            var prodSection = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var prodStack = new StackPanel { Spacing = 12 };
            prodStack.Children.Add(new TextBlock { Text = "Productivity", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            var metricsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30 };
            metricsRow.Children.Add(CreateShopDocSmallMetric("Working Days", productivity.WorkingDays.ToString()));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Invoices/Day", productivity.AvgInvoicesPerDay.ToString("F1")));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Value/Day", productivity.AvgValuePerDay.ToString("C0")));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Peak Hour", productivity.FormattedProductiveHour));
            prodStack.Children.Add(metricsRow);
            prodSection.Child = prodStack;
            stack.Children.Add(prodSection);

            // Most used docs
            if (topDocs.Any())
            {
                var docsSection = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
                var docsStack = new StackPanel { Spacing = 8 };
                docsStack.Children.Add(new TextBlock { Text = "Most Created Documents", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });
                var maxUsage = topDocs.Max(d => d.UsageCount);
                foreach (var doc in topDocs.Take(8))
                    docsStack.Children.Add(CreateShopDocUsageBar(doc.Name, doc.UsageCount, maxUsage));
                docsSection.Child = docsStack;
                stack.Children.Add(docsSection);
            }

            // Recent activity
            if (recentActivity.Any())
            {
                var actSection = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
                var actStack = new StackPanel { Spacing = 6 };
                actStack.Children.Add(new TextBlock { Text = "Recent Activity", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });
                foreach (var activity in recentActivity.Take(12))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

                    var icon = new FontIcon { Glyph = activity.Icon, FontSize = 11, Foreground = new SolidColorBrush(activity.ActivityType == "Invoice" ? AccentGreen : AccentBlue) };
                    Grid.SetColumn(icon, 0); row.Children.Add(icon);

                    var desc = new TextBlock { Text = activity.Description, FontSize = 11, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(desc, 1); row.Children.Add(desc);

                    if (activity.Value > 0)
                    {
                        var val = new TextBlock { Text = activity.Value.ToString("C0"), FontSize = 10, Foreground = new SolidColorBrush(AccentGreen), Margin = new Thickness(8, 0, 0, 0) };
                        Grid.SetColumn(val, 2); row.Children.Add(val);
                    }

                    var time = new TextBlock { Text = activity.FormattedDate, FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)), HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(time, 3); row.Children.Add(time);

                    actStack.Children.Add(row);
                }
                actSection.Child = actStack;
                stack.Children.Add(actSection);
            }

            return stack;
        }

        private Border BuildSummaryHeader(StatsPeriod period)
        {
            var exportStats = _exportStats.GetCombinedStatsByUser(period, _currentUserId);
            var docSummary = DocumentUsageTrackingService.Instance.GetUsageSummary(period, _currentUserId);

            // Daily data for sparklines
            var dailyExport = _exportStats.GetDailyBreakdownByUser(_currentUserId).Take(14).Reverse().ToList();
            var dailyInvoice = DocumentUsageTrackingService.Instance.GetDailyActivity(period, _currentUserId).Take(14).ToList();
            var estSpark = dailyExport.Select(d => (double)d.ExportCount).ToList();
            var opsSpark = dailyExport.Select(d => (double)d.ExportOperations).ToList();
            var valSpark = dailyExport.Select(d => (double)d.ExportPrice).ToList();
            var invSpark = dailyInvoice.Select(d => (double)d.InvoiceCount).ToList();
            var invValSpark = dailyInvoice.Select(d => (double)d.InvoiceValue).ToList();

            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = { new GradientStop { Color = Color.FromArgb(255, 30, 50, 70), Offset = 0 }, new GradientStop { Color = Color.FromArgb(255, 40, 35, 60), Offset = 1 } }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var grid = new Grid();
            for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var est = CreateHeaderMetric("Estimates", exportStats.ExportCount.ToString(), AccentBlue, exportStats.ExportCount, "N0", estSpark);
            var ops = CreateHeaderMetric("Operations", exportStats.ExportOperations.ToString(), AccentGreen, exportStats.ExportOperations, "N0", opsSpark);
            var val = CreateHeaderMetric("Export Value", exportStats.FormattedExportPrice, AccentOrange, (double)exportStats.ExportPrice, "C0", valSpark);
            var inv = CreateHeaderMetric("Invoices", docSummary.TotalInvoices.ToString(), AccentPurple, docSummary.TotalInvoices, "N0", invSpark);
            var invVal = CreateHeaderMetric("Invoice Value", docSummary.FormattedInvoiceValue, AccentCyan, (double)docSummary.TotalInvoiceValue, "C0", invValSpark);

            Grid.SetColumn(est, 0); Grid.SetColumn(ops, 1); Grid.SetColumn(val, 2); Grid.SetColumn(inv, 3); Grid.SetColumn(invVal, 4);
            grid.Children.Add(est); grid.Children.Add(ops); grid.Children.Add(val); grid.Children.Add(inv); grid.Children.Add(invVal);

            border.Child = grid;
            return border;
        }

        private StackPanel CreateHeaderMetric(string label, string value, Color accent, double rawValue = 0, string format = "", List<double>? sparkData = null)
        {
            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            var valueBlock = new TextBlock { Text = value, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(valueBlock);
            if (rawValue > 0 && !string.IsNullOrEmpty(format))
                AnimateCountUp(valueBlock, rawValue, format);
            stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)), HorizontalAlignment = HorizontalAlignment.Center });
            if (sparkData != null && sparkData.Count >= 2)
                AddSparkline(stack, sparkData, accent);
            return stack;
        }

        private Border BuildBusinessOutcomesPanel(StatsPeriod period)
        {
            var enhancedStats = _exportStats.GetEnhancedStats(_currentUserId, period);
            var roi = _exportStats.GetROIStats(_currentUserId, period);
            var breakdown = _exportStats.GetCategoryBreakdown(_currentUserId, period);
            var achievements = _exportStats.GetAchievements(_currentUserId);
            var partStats = _exportStats.GetPartTypeBreakdown(_currentUserId, period);

            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 25, 40, 55), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 35, 30, 50), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 14 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE9D2", FontSize = 16, Foreground = new SolidColorBrush(AccentCyan) });
            header.Children.Add(new TextBlock
            {
                Text = "BUSINESS OUTCOMES",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Key metrics row (3 columns)
            var metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Total Value
            var totalStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            totalStack.Children.Add(new TextBlock
            {
                Text = enhancedStats.FormattedValue,
                FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGreen),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            totalStack.Children.Add(new TextBlock
            {
                Text = "Total Value",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(totalStack, 0);
            metricsGrid.Children.Add(totalStack);

            // Avg Per Estimate
            var avgStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            avgStack.Children.Add(new TextBlock
            {
                Text = enhancedStats.FormattedAvgValue,
                FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentBlue),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            avgStack.Children.Add(new TextBlock
            {
                Text = "Avg Per Estimate",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(avgStack, 1);
            metricsGrid.Children.Add(avgStack);

            // Trend
            var trendStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var trendValue = enhancedStats.ValueTrend;
            var trendColor = trendValue >= 0 ? AccentGreen : AccentRed;
            var trendArrow = trendValue >= 0 ? "\u25B2" : "\u25BC";
            trendStack.Children.Add(new TextBlock
            {
                Text = $"{trendArrow} {Math.Abs(trendValue):F1}%",
                FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(trendColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            trendStack.Children.Add(new TextBlock
            {
                Text = "Value Trend",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(trendStack, 2);
            metricsGrid.Children.Add(trendStack);

            stack.Children.Add(metricsGrid);

            // ROI section
            stack.Children.Add(CreateROISection(roi));

            // Category breakdown
            stack.Children.Add(CreateCategoryBreakdownSection(breakdown));

            // Top 5 parts by value
            var topParts = partStats.OrderByDescending(p => p.TotalValue).Take(5).ToList();
            if (topParts.Any())
            {
                var partsSection = new StackPanel { Spacing = 6 };
                var partsHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                partsHeader.Children.Add(new FontIcon { Glyph = "\uE8EC", FontSize = 14, Foreground = new SolidColorBrush(AccentOrange) });
                partsHeader.Children.Add(new TextBlock
                {
                    Text = "TOP PARTS BY VALUE",
                    FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    CharacterSpacing = 40
                });
                partsSection.Children.Add(partsHeader);

                var maxVal = topParts.Max(p => p.TotalValue);
                int barIdx = 0;
                foreach (var part in topParts)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    row.Children.Add(new TextBlock
                    {
                        Text = part.PartType,
                        FontSize = 11, Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    var targetWidth = maxVal > 0 ? (double)(part.TotalValue / maxVal) * 160 : 0;
                    var bar = new Border
                    {
                        Width = 0, Height = 12,
                        Background = new SolidColorBrush(AccentOrange),
                        CornerRadius = new CornerRadius(2),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Grid.SetColumn(bar, 1);
                    row.Children.Add(bar);
                    AnimateBarWidth(bar, targetWidth, delayMs: barIdx * 60);

                    var valText = new TextBlock
                    {
                        Text = part.TotalValue.ToString("C0"),
                        FontSize = 10, Foreground = new SolidColorBrush(AccentGreen),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(valText, 2);
                    row.Children.Add(valText);

                    partsSection.Children.Add(row);
                    barIdx++;
                }
                stack.Children.Add(partsSection);
            }

            // Achievements
            stack.Children.Add(CreateAchievementsSection(achievements));

            border.Child = stack;
            return border;
        }

        private Border BuildExportStatsSection(StatsPeriod period)
        {
            var exportStats = _exportStats.GetCombinedStatsByUser(period, _currentUserId);
            var enhancedStats = _exportStats.GetEnhancedStats(_currentUserId, period);

            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = "Export Statistics", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });

            var statsGrid = new Grid();
            for (int i = 0; i < 4; i++) statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var est = CreateMiniStatCard("Estimates", exportStats.ExportCount.ToString(), AccentBlue, exportStats.ExportCount, "N0");
            var ops = CreateMiniStatCard("Operations", exportStats.ExportOperations.ToString(), AccentGreen, exportStats.ExportOperations, "N0");
            var labor = CreateMiniStatCard("Labor Hours", enhancedStats.TotalLaborHours.ToString("F1"), AccentOrange, (double)enhancedStats.TotalLaborHours, "F1");
            var val = CreateMiniStatCard("Total Value", exportStats.FormattedExportPrice, AccentPurple, (double)exportStats.ExportPrice, "C0");

            Grid.SetColumn(est, 0); Grid.SetColumn(ops, 1); Grid.SetColumn(labor, 2); Grid.SetColumn(val, 3);
            statsGrid.Children.Add(est); statsGrid.Children.Add(ops); statsGrid.Children.Add(labor); statsGrid.Children.Add(val);
            stack.Children.Add(statsGrid);

            var metricsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 8, 0, 0) };
            metricsRow.Children.Add(CreateShopDocSmallMetric("Avg Ops/Est", enhancedStats.AvgOperationsPerEstimate.ToString("F1")));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Avg Value", enhancedStats.FormattedAvgValue));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Sessions", enhancedStats.TotalSessions.ToString()));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Time Spent", enhancedStats.FormattedTimeSpent));
            stack.Children.Add(metricsRow);

            border.Child = stack;
            return border;
        }

        private Border BuildQuickOperationsSection(StatsPeriod period)
        {
            var mostUsed = _exportStats.GetMostUsedOperationsByUser(_currentUserId, period);

            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Top Operations", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });

            if (!mostUsed.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxCount = mostUsed.Max(o => o.Count);
                int barIndex = 0;
                foreach (var op in mostUsed.Take(6))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                    row.Children.Add(new TextBlock { Text = op.OperationType, FontSize = 11, Foreground = new SolidColorBrush(AccentBlue), VerticalAlignment = VerticalAlignment.Center });

                    var targetWidth = (double)op.Count / maxCount * 180;
                    var bar = new Border { Width = 0, Height = 12, Background = new SolidColorBrush(AccentGreen), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
                    Grid.SetColumn(bar, 1); row.Children.Add(bar);
                    AnimateBarWidth(bar, targetWidth, delayMs: barIndex * 60);

                    var cnt = new TextBlock { Text = op.Count.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cnt, 2); row.Children.Add(cnt);

                    stack.Children.Add(row);
                    barIndex++;
                }
            }

            border.Child = stack;
            return border;
        }

        private Border BuildRecentActivitySection(StatsPeriod period)
        {
            var recentExports = _exportStats.GetRecentTransactionsByUser(_currentUserId, 8)
                .Where(t => t.Type == TransactionType.Export).ToList();

            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Recent Exports", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 8) });

            if (!recentExports.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                foreach (var export in recentExports)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });

                    row.Children.Add(new TextBlock { Text = $"{export.OperationCount} operations", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });

                    var val = new TextBlock { Text = export.TotalPrice.ToString("C0"), FontSize = 11, Foreground = new SolidColorBrush(AccentGreen), HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(val, 1); row.Children.Add(val);

                    var time = new TextBlock { Text = export.Timestamp.ToString("MMM d h:mm tt"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)), HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(time, 2); row.Children.Add(time);

                    stack.Children.Add(row);
                }
            }

            border.Child = stack;
            return border;
        }

        private StackPanel CreateMiniStatCard(string label, string value, Color accent, double rawValue = 0, string format = "")
        {
            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(8) };
            var valueBlock = new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(valueBlock);
            if (rawValue > 0 && !string.IsNullOrEmpty(format))
                AnimateCountUp(valueBlock, rawValue, format);
            stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), HorizontalAlignment = HorizontalAlignment.Center });
            return stack;
        }

        private StackPanel CreateInvoiceTypeMini(string title, int count, decimal value, Color accent)
        {
            var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(8) };
            stack.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = value.ToString("C0"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)), HorizontalAlignment = HorizontalAlignment.Center });
            return stack;
        }

        #endregion

        #region Overview Tab

        private StackPanel BuildOverviewTab()
        {
            var stack = new StackPanel { Spacing = 12 };
            var period = GetSelectedPeriod();
            var exportStats = _exportStats.GetCombinedStatsByUser(period, _currentUserId);
            var estimateStats = _estimateStats.GetStatsByUser(period, _currentUserId);
            var enhancedStats = _exportStats.GetEnhancedStats(_currentUserId, period);
            var performanceRanking = _exportStats.GetPerformanceRanking(_currentUserId, period);
            var goals = _exportStats.GetUserGoals(_currentUserId);
            var roi = _exportStats.GetROIStats(_currentUserId, period);
            var categoryBreakdown = _exportStats.GetCategoryBreakdown(_currentUserId, period);
            var achievements = _exportStats.GetAchievements(_currentUserId);
            var insights = _exportStats.GetSmartInsights(_currentUserId);
            var weekComparison = _exportStats.GetWeeklyComparison(_currentUserId);

            // User header with streaks AND performance ranking
            stack.Children.Add(CreateUserHeaderWithRanking(enhancedStats, performanceRanking));

            // Smart insights section (unique to McStud!)
            if (insights.Count > 0)
            {
                stack.Children.Add(CreateSmartInsightsSection(insights));
            }

            // Goal progress section (daily targets)
            stack.Children.Add(CreateGoalProgressSection(goals));

            // Week-over-week comparison (unique!)
            stack.Children.Add(CreateWeekComparisonSection(weekComparison));

            // Summary cards row 1 - with trends
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var estimatesCard = CreateStatCardWithTrend("ESTIMATES", enhancedStats.TotalEstimates.ToString(),
                $"Avg {enhancedStats.AvgOperationsPerEstimate:F1} ops/est", AccentBlue, "\uE8A1", enhancedStats.EstimatesTrend, enhancedStats.TotalEstimates, "N0");
            var opsCard = CreateStatCardWithTrend("OPERATIONS", enhancedStats.TotalOperations.ToString(),
                $"{enhancedStats.FormattedAvgValue}/avg", AccentGreen, "\uE8B8", enhancedStats.OperationsTrend, enhancedStats.TotalOperations, "N0");
            var valueCard = CreateStatCardWithTrend("TOTAL VALUE", enhancedStats.FormattedValue,
                $"{enhancedStats.AvgValuePerOperation:C0}/op", AccentOrange, "\uE8C8", enhancedStats.ValueTrend, (double)enhancedStats.TotalValue, "C0");

            Grid.SetColumn(estimatesCard, 0);
            Grid.SetColumn(opsCard, 2);
            Grid.SetColumn(valueCard, 4);
            row1.Children.Add(estimatesCard);
            row1.Children.Add(opsCard);
            row1.Children.Add(valueCard);
            stack.Children.Add(row1);

            // Summary cards row 2 - sessions and time
            var row2 = new Grid();
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var laborCard = CreateStatCard("LABOR HRS", enhancedStats.TotalLaborHours.ToString("F1"),
                $"Exported", AccentPurple, "\uE823", (double)enhancedStats.TotalLaborHours, "F1");
            var sessionsCard = CreateStatCard("SESSIONS", enhancedStats.TotalSessions.ToString(),
                $"Avg {enhancedStats.AvgEstimatesPerSession:F1} est/session", AccentCyan, "\uE7EF", enhancedStats.TotalSessions, "N0");
            var timeCard = CreateStatCard("TIME SPENT", enhancedStats.FormattedTimeSpent,
                $"Avg {(enhancedStats.AvgSessionDuration.TotalMinutes > 0 ? enhancedStats.AvgSessionDuration.TotalMinutes.ToString("F0") + "m" : "0m")}/session", Color.FromArgb(255, 100, 200, 150), "\uE916");

            Grid.SetColumn(laborCard, 0);
            Grid.SetColumn(sessionsCard, 2);
            Grid.SetColumn(timeCard, 4);
            row2.Children.Add(laborCard);
            row2.Children.Add(sessionsCard);
            row2.Children.Add(timeCard);
            stack.Children.Add(row2);

            // ROI Calculator section
            stack.Children.Add(CreateROISection(roi));

            // Category breakdown (Labor vs Refinish vs Materials)
            stack.Children.Add(CreateCategoryBreakdownSection(categoryBreakdown));

            // Target breakdown
            stack.Children.Add(CreateTargetBreakdown(exportStats));

            // Shop-wide summary (all users combined)
            stack.Children.Add(CreateShopSummarySection(period));

            // Top parts breakdown
            stack.Children.Add(CreateTopPartsSection());

            // Top added operations
            stack.Children.Add(CreateTopAddedOperationsSection());

            // Achievements section (unique to McStud!)
            stack.Children.Add(CreateAchievementsSection(achievements));

            // Mini chart
            stack.Children.Add(CreateMiniChart());

            // Shop Documents & Invoices section
            stack.Children.Add(CreateShopDocsStatsSection(period));

            // Learning mode stats section
            stack.Children.Add(CreateLearningModeStatsSection());

            // Version footer
            stack.Children.Add(new TextBlock
            {
                Text = $"McStud Tool {UpdateService.GetVersionString()} • {_currentUserId}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return stack;
        }

        private Border CreateShopDocsStatsSection(StatsPeriod period)
        {
            var docTracker = DocumentUsageTrackingService.Instance;
            var summary = docTracker.GetUsageSummary(period, _currentUserId);
            var invoiceStats = docTracker.GetInvoiceStats(period, _currentUserId);
            var topDocs = docTracker.GetMostUsedDocuments(period, 5, _currentUserId);
            var productivity = docTracker.GetProductivityMetrics(period, _currentUserId);

            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Section header
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerRow.Children.Add(new FontIcon
            {
                Glyph = "\uE8C7",
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentOrange)
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = "Shop Documents & Invoices",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            mainStack.Children.Add(headerRow);

            // Summary row - invoice counts
            var summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var totalInvoices = CreateShopDocMetric("Invoices", summary.TotalInvoices.ToString(), AccentGreen);
            var totalValue = CreateShopDocMetric("Total Value", summary.FormattedInvoiceValue, AccentBlue);
            var colorTint = CreateShopDocMetric("Color Tint", summary.ColorTintInvoices.ToString(), Color.FromArgb(255, 100, 180, 255));
            var shopStock = CreateShopDocMetric("Shop Stock", summary.ShopStockInvoices.ToString(), Color.FromArgb(255, 100, 200, 100));
            var towBills = CreateShopDocMetric("Tow Bills", summary.TowBills.ToString(), Color.FromArgb(255, 255, 180, 100));

            Grid.SetColumn(totalInvoices, 0);
            Grid.SetColumn(totalValue, 1);
            Grid.SetColumn(colorTint, 2);
            Grid.SetColumn(shopStock, 3);
            Grid.SetColumn(towBills, 4);
            summaryGrid.Children.Add(totalInvoices);
            summaryGrid.Children.Add(totalValue);
            summaryGrid.Children.Add(colorTint);
            summaryGrid.Children.Add(shopStock);
            summaryGrid.Children.Add(towBills);
            mainStack.Children.Add(summaryGrid);

            // Metrics row
            var metricsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(0, 4, 0, 0) };
            metricsRow.Children.Add(CreateShopDocSmallMetric("Avg Value", invoiceStats.FormattedAvg));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Avg Items", invoiceStats.AvgItems.ToString("F1")));
            metricsRow.Children.Add(CreateShopDocSmallMetric("PDFs Exported", summary.TotalPdfExports.ToString()));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Peak Hour", productivity.FormattedProductiveHour));
            metricsRow.Children.Add(CreateShopDocSmallMetric("Working Days", productivity.WorkingDays.ToString()));
            mainStack.Children.Add(metricsRow);

            // Most used documents (if any)
            if (topDocs.Any())
            {
                var docsHeader = new TextBlock
                {
                    Text = "Most Created Documents",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                mainStack.Children.Add(docsHeader);

                var maxUsage = topDocs.Max(d => d.UsageCount);
                foreach (var doc in topDocs.Take(4))
                {
                    mainStack.Children.Add(CreateShopDocUsageBar(doc.Name, doc.UsageCount, maxUsage));
                }
            }

            border.Child = mainStack;
            return border;
        }

        private Border CreateLearningModeStatsSection()
        {
            var currentMode = LearningModeService.Instance.CurrentMode;
            var activePatterns = EstimateLearningService.Instance.CurrentDatabase.Patterns.Count;
            var activeExamples = EstimateLearningService.Instance.CurrentDatabase.TrainingExamples.Count;

            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE7BE",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentCyan)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Learning Mode",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Active mode badge
            var modeBadge = new Border
            {
                Background = new SolidColorBrush(currentMode == LearningMode.Shop
                    ? Color.FromArgb(255, 0, 100, 180)
                    : Color.FromArgb(255, 140, 80, 200)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2)
            };
            modeBadge.Child = new TextBlock
            {
                Text = currentMode == LearningMode.Shop ? "Standard" : "Personal",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            };
            header.Children.Add(modeBadge);
            mainStack.Children.Add(header);

            // Stats row
            var statsRow = new Grid();
            statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Standard stats
            var standardStack = new StackPanel { Spacing = 4 };
            var standardLabel = new TextBlock
            {
                Text = "STANDARD",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            standardStack.Children.Add(standardLabel);

            int standardPatterns = 0;
            int standardExamples = 0;
            try
            {
                var baseKnowledgePath = EstimateLearningService.Instance.BaseKnowledgePath;
                if (System.IO.File.Exists(baseKnowledgePath))
                {
                    var json = System.IO.File.ReadAllText(baseKnowledgePath);
                    var baseDb = System.Text.Json.JsonSerializer.Deserialize<LearnedPatternDatabase>(json);
                    if (baseDb != null)
                    {
                        standardPatterns = baseDb.Patterns.Count;
                        standardExamples = baseDb.TrainingExamples.Count;
                    }
                }
            }
            catch { }

            standardStack.Children.Add(new TextBlock
            {
                Text = standardPatterns.ToString(),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(currentMode == LearningMode.Shop ? AccentBlue : Color.FromArgb(255, 100, 100, 100))
            });
            standardStack.Children.Add(new TextBlock
            {
                Text = $"patterns • {standardExamples} examples",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            if (currentMode == LearningMode.Shop)
            {
                standardStack.Children.Add(new TextBlock
                {
                    Text = "Active",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(AccentGreen),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
            }
            Grid.SetColumn(standardStack, 0);
            statsRow.Children.Add(standardStack);

            // Personal stats
            var personalStack = new StackPanel { Spacing = 4 };
            personalStack.Children.Add(new TextBlock
            {
                Text = "PERSONAL",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            int personalPatterns = 0;
            int personalExamples = 0;
            try
            {
                var userKnowledgePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "McStudDesktop", "learned_patterns.json");
                if (System.IO.File.Exists(userKnowledgePath))
                {
                    var json = System.IO.File.ReadAllText(userKnowledgePath);
                    if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "null")
                    {
                        var userDb = System.Text.Json.JsonSerializer.Deserialize<LearnedPatternDatabase>(json);
                        if (userDb != null)
                        {
                            personalPatterns = userDb.Patterns.Count;
                            personalExamples = userDb.TrainingExamples.Count;
                        }
                    }
                }
            }
            catch { }

            personalStack.Children.Add(new TextBlock
            {
                Text = personalPatterns.ToString(),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(currentMode == LearningMode.Personal ? Color.FromArgb(255, 180, 120, 255) : Color.FromArgb(255, 100, 100, 100))
            });
            personalStack.Children.Add(new TextBlock
            {
                Text = $"patterns • {personalExamples} examples",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            if (currentMode == LearningMode.Personal)
            {
                personalStack.Children.Add(new TextBlock
                {
                    Text = "Active",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(AccentGreen),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
            }
            Grid.SetColumn(personalStack, 1);
            statsRow.Children.Add(personalStack);

            mainStack.Children.Add(statsRow);

            border.Child = mainStack;
            return border;
        }

        private StackPanel CreateShopDocMetric(string label, string value, Color accentColor)
        {
            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return stack;
        }

        private StackPanel CreateShopDocSmallMetric(string label, string value)
        {
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            return stack;
        }

        private Grid CreateShopDocUsageBar(string name, int count, int maxCount)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            var barWidth = maxCount > 0 ? (double)count / maxCount * 150 : 0;
            var bar = new Border
            {
                Width = barWidth,
                Height = 10,
                Background = new SolidColorBrush(AccentOrange),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(bar, 1);
            grid.Children.Add(bar);

            var countText = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(countText, 2);
            grid.Children.Add(countText);

            return grid;
        }

        private Border CreateUserHeaderWithStreaks(EnhancedStats stats)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 30, 50, 70), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 40, 35, 60), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // User avatar
            var avatar = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(AccentBlue)
            };
            avatar.Child = new TextBlock
            {
                Text = _currentUserId.Length > 0 ? _currentUserId[0].ToString().ToUpper() : "?",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(avatar);

            // User info
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = _currentUserId,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Viewing {GetSelectedPeriod()} statistics",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            stack.Children.Add(infoStack);

            Grid.SetColumn(stack, 0);
            mainGrid.Children.Add(stack);

            // Streaks section
            var streakStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };

            // Current streak
            var currentStreakStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            currentStreakStack.Children.Add(new TextBlock
            {
                Text = stats.CurrentStreak > 0 ? "\uE735" : "\uE734",  // Fire or empty
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = new SolidColorBrush(stats.CurrentStreak > 0 ? AccentOrange : Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            currentStreakStack.Children.Add(new TextBlock
            {
                Text = $"{stats.CurrentStreak} day{(stats.CurrentStreak != 1 ? "s" : "")}",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            currentStreakStack.Children.Add(new TextBlock
            {
                Text = "streak",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            streakStack.Children.Add(currentStreakStack);

            // Best streak
            var bestStreakStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            bestStreakStack.Children.Add(new TextBlock
            {
                Text = "\uE734",  // Trophy
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            bestStreakStack.Children.Add(new TextBlock
            {
                Text = stats.LongestStreak.ToString(),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            bestStreakStack.Children.Add(new TextBlock
            {
                Text = "best",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            streakStack.Children.Add(bestStreakStack);

            Grid.SetColumn(streakStack, 1);
            mainGrid.Children.Add(streakStack);

            border.Child = mainGrid;
            return border;
        }

        private Border CreateUserHeaderWithRanking(EnhancedStats stats, PerformanceRanking ranking)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 30, 50, 70), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 40, 35, 60), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // User info (left)
            var userStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var avatar = new Border
            {
                Width = 40, Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(AccentBlue)
            };
            avatar.Child = new TextBlock
            {
                Text = _currentUserId.Length > 0 ? _currentUserId[0].ToString().ToUpper() : "?",
                FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            userStack.Children.Add(avatar);

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = _currentUserId,
                FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Viewing {GetSelectedPeriod()} statistics",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            userStack.Children.Add(infoStack);
            Grid.SetColumn(userStack, 0);
            mainGrid.Children.Add(userStack);

            // Performance ranking (center-right)
            var rankingStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
            var rankColor = ranking.IsTopPerformer ? AccentGreen : AccentOrange;
            var rankBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(60, rankColor.R, rankColor.G, rankColor.B)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var rankContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            rankContent.Children.Add(new FontIcon
            {
                Glyph = ranking.IsTopPerformer ? "\uE734" : "\uE7C1", // Trophy or chart
                FontSize = 14,
                Foreground = new SolidColorBrush(rankColor)
            });
            rankContent.Children.Add(new TextBlock
            {
                Text = ranking.PercentileLabel,
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(rankColor)
            });
            rankBadge.Child = rankContent;
            rankingStack.Children.Add(rankBadge);
            rankingStack.Children.Add(new TextBlock
            {
                Text = $"#{ranking.Rank} of {ranking.TotalUsers} users",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(rankingStack, 1);
            mainGrid.Children.Add(rankingStack);

            // Streaks (right)
            var streakStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };

            // Current streak
            var currentStreakStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            currentStreakStack.Children.Add(new TextBlock
            {
                Text = stats.CurrentStreak > 0 ? "\uE735" : "\uE734",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = new SolidColorBrush(stats.CurrentStreak > 0 ? AccentOrange : Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            currentStreakStack.Children.Add(new TextBlock
            {
                Text = $"{stats.CurrentStreak}d",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            streakStack.Children.Add(currentStreakStack);

            Grid.SetColumn(streakStack, 2);
            mainGrid.Children.Add(streakStack);

            border.Child = mainGrid;
            return border;
        }

        private Border CreateGoalProgressSection(UserGoals goals)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE9D5", FontSize = 14, Foreground = new SolidColorBrush(AccentCyan) });
            header.Children.Add(new TextBlock
            {
                Text = "TODAY'S GOALS",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Goals grid
            var goalsGrid = new Grid();
            goalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            goalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            goalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            goalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            goalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Operations goal
            var opsGoal = CreateGoalProgressBar("Operations", goals.CurrentDayOperations, goals.DailyOperationsTarget, AccentGreen);
            Grid.SetColumn(opsGoal, 0);
            goalsGrid.Children.Add(opsGoal);

            // Estimates goal
            var estGoal = CreateGoalProgressBar("Estimates", goals.CurrentDayEstimates, goals.DailyEstimatesTarget, AccentBlue);
            Grid.SetColumn(estGoal, 2);
            goalsGrid.Children.Add(estGoal);

            // Value goal
            var valueGoal = CreateGoalProgressBar("Value", (int)goals.CurrentDayValue, (int)goals.DailyValueTarget, AccentOrange, true);
            Grid.SetColumn(valueGoal, 4);
            goalsGrid.Children.Add(valueGoal);

            stack.Children.Add(goalsGrid);

            border.Child = stack;
            return border;
        }

        private StackPanel CreateGoalProgressBar(string label, int current, int target, Color color, bool isCurrency = false)
        {
            var stack = new StackPanel { Spacing = 4 };
            var progress = target > 0 ? Math.Min(100, (double)current / target * 100) : 0;
            var isComplete = current >= target;

            // Label row
            var labelRow = new Grid();
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            labelRow.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });

            var valueText = new TextBlock
            {
                Text = isCurrency ? $"${current:N0}" : current.ToString(),
                FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isComplete ? AccentGreen : Colors.White),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueText, 1);
            labelRow.Children.Add(valueText);
            stack.Children.Add(labelRow);

            // Progress bar
            var barBg = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
            };
            var barFill = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(isComplete ? AccentGreen : color),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(2, progress * 1.2) // Scale to ~120px max
            };
            barBg.Child = barFill;
            stack.Children.Add(barBg);

            // Target text
            stack.Children.Add(new TextBlock
            {
                Text = $"/ {(isCurrency ? $"${target:N0}" : target.ToString())} ({progress:F0}%)",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });

            return stack;
        }

        private Border CreateROISection(ROIStats roi)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 35, 55, 35), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 40, 45, 50), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var stack = new StackPanel { Spacing = 12 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 16, Foreground = new SolidColorBrush(AccentGreen) });
            header.Children.Add(new TextBlock
            {
                Text = "ROI CALCULATOR",
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            header.Children.Add(new TextBlock
            {
                Text = "McStud Impact",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            stack.Children.Add(header);

            // Main stats grid
            var statsGrid = new Grid();
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Value captured
            var valueStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            valueStack.Children.Add(new TextBlock
            {
                Text = roi.FormattedValueCaptured,
                FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGreen),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            valueStack.Children.Add(new TextBlock
            {
                Text = "Value Captured",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(valueStack, 0);
            statsGrid.Children.Add(valueStack);

            // Estimated missed
            var missedStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            missedStack.Children.Add(new TextBlock
            {
                Text = roi.FormattedMissed,
                FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentOrange),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            missedStack.Children.Add(new TextBlock
            {
                Text = "Est. Saved",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(missedStack, 1);
            statsGrid.Children.Add(missedStack);

            // Average per estimate
            var avgStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            avgStack.Children.Add(new TextBlock
            {
                Text = roi.FormattedAvgPerEstimate,
                FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentBlue),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            avgStack.Children.Add(new TextBlock
            {
                Text = "Avg/Estimate",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(avgStack, 2);
            statsGrid.Children.Add(avgStack);

            // Time saved
            var timeStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            timeStack.Children.Add(new TextBlock
            {
                Text = roi.FormattedTimeSaved,
                FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentPurple),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            timeStack.Children.Add(new TextBlock
            {
                Text = "Time Saved",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(timeStack, 3);
            statsGrid.Children.Add(timeStack);

            stack.Children.Add(statsGrid);

            border.Child = stack;
            return border;
        }

        private Border CreateCategoryBreakdownSection(CategoryBreakdown breakdown)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE9F9", FontSize = 14, Foreground = new SolidColorBrush(AccentPurple) });
            header.Children.Add(new TextBlock
            {
                Text = "CATEGORY BREAKDOWN",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            if (breakdown.TotalDollars == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No data yet",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
            else
            {
                // Visual bar chart
                var categories = new[]
                {
                    ("Labor", breakdown.LaborDollars, breakdown.LaborPercent, AccentBlue),
                    ("Refinish", breakdown.RefinishDollars, breakdown.RefinishPercent, AccentPurple),
                    ("Materials", breakdown.MaterialsDollars, breakdown.MaterialsPercent, AccentOrange),
                    ("Other", breakdown.OtherDollars, breakdown.OtherPercent, Color.FromArgb(255, 100, 100, 100))
                };

                foreach (var (name, value, percent, color) in categories)
                {
                    if (value <= 0) continue;

                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Name
                    row.Children.Add(new TextBlock
                    {
                        Text = name,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // Bar
                    var barContainer = new Border
                    {
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                        Margin = new Thickness(8, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var barFill = new Border
                    {
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(color),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = Math.Max(4, percent * 2) // Scale bar
                    };
                    barContainer.Child = barFill;
                    Grid.SetColumn(barContainer, 1);
                    row.Children.Add(barContainer);

                    // Value and percent
                    var valueText = new TextBlock
                    {
                        Text = $"{value:C0} ({percent:F0}%)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(color),
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 90,
                        TextAlignment = TextAlignment.Right
                    };
                    Grid.SetColumn(valueText, 2);
                    row.Children.Add(valueText);

                    stack.Children.Add(row);
                }
            }

            border.Child = stack;
            return border;
        }

        private Border CreateTopAddedOperationsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE8B8", FontSize = 14, Foreground = new SolidColorBrush(AccentGreen) });
            header.Children.Add(new TextBlock
            {
                Text = "TOP OPERATIONS ADDED",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            var period = GetSelectedPeriod();
            var topOps = _exportStats.GetTopAddedOperations(_currentUserId, period, 5);

            if (topOps.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No data yet",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
            else
            {
                var maxCount = topOps.Max(o => o.TimesAdded);
                foreach (var op in topOps)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Operation type
                    row.Children.Add(new TextBlock
                    {
                        Text = op.OperationType.Length > 10 ? op.OperationType.Substring(0, 10) : op.OperationType,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // Bar
                    var barBg = new Border
                    {
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                        Margin = new Thickness(8, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var barFill = new Border
                    {
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(AccentGreen),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = Math.Max(8, (double)op.TimesAdded / maxCount * 150)
                    };
                    barBg.Child = barFill;
                    Grid.SetColumn(barBg, 1);
                    row.Children.Add(barBg);

                    // Count
                    var countText = new TextBlock
                    {
                        Text = $"{op.TimesAdded}x • {op.FormattedAvgValue}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 80,
                        TextAlignment = TextAlignment.Right
                    };
                    Grid.SetColumn(countText, 2);
                    row.Children.Add(countText);

                    stack.Children.Add(row);
                }
            }

            border.Child = stack;
            return border;
        }

        private Border CreateSmartInsightsSection(List<SmartInsight> insights)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 40, 50, 60), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 50, 40, 55), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE82F", FontSize = 14, Foreground = new SolidColorBrush(AccentCyan) });
            header.Children.Add(new TextBlock
            {
                Text = "SMART INSIGHTS",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Insights list
            foreach (var insight in insights)
            {
                var insightColor = ColorFromHex(insight.Color);
                var insightRow = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, insightColor.R, insightColor.G, insightColor.B)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var rowContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                rowContent.Children.Add(new FontIcon
                {
                    Glyph = insight.Icon,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(insightColor)
                });

                var textStack = new StackPanel();
                textStack.Children.Add(new TextBlock
                {
                    Text = insight.Title,
                    FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = insight.Message,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                    TextWrapping = TextWrapping.Wrap
                });
                rowContent.Children.Add(textStack);

                insightRow.Child = rowContent;
                stack.Children.Add(insightRow);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateWeekComparisonSection(WeeklyComparison comparison)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE8B5", FontSize = 14, Foreground = new SolidColorBrush(AccentBlue) });
            header.Children.Add(new TextBlock
            {
                Text = "THIS WEEK VS LAST WEEK",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Comparison grid
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Operations comparison
            var opsStack = CreateComparisonItem("Operations",
                comparison.ThisWeekOperations, comparison.LastWeekOperations,
                comparison.OperationsChange, AccentGreen);
            Grid.SetColumn(opsStack, 0);
            grid.Children.Add(opsStack);

            // Value comparison
            var valueStack = CreateComparisonItem("Value",
                (int)comparison.ThisWeekValue, (int)comparison.LastWeekValue,
                comparison.ValueChange, AccentOrange, true);
            Grid.SetColumn(valueStack, 1);
            grid.Children.Add(valueStack);

            // Estimates comparison
            var estChange = comparison.LastWeekEstimates > 0
                ? (double)(comparison.ThisWeekEstimates - comparison.LastWeekEstimates) / comparison.LastWeekEstimates * 100 : 0;
            var estStack = CreateComparisonItem("Estimates",
                comparison.ThisWeekEstimates, comparison.LastWeekEstimates,
                estChange, AccentBlue);
            Grid.SetColumn(estStack, 2);
            grid.Children.Add(estStack);

            stack.Children.Add(grid);

            border.Child = stack;
            return border;
        }

        private StackPanel CreateComparisonItem(string label, int thisWeek, int lastWeek, double changePercent, Color color, bool isCurrency = false)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var isUp = changePercent > 0;
            var changeColor = isUp ? AccentGreen : AccentRed;

            // This week value
            stack.Children.Add(new TextBlock
            {
                Text = isCurrency ? $"${thisWeek:N0}" : thisWeek.ToString(),
                FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Label
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Change indicator
            if (Math.Abs(changePercent) > 0.5)
            {
                var changeStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                changeStack.Children.Add(new FontIcon
                {
                    Glyph = isUp ? "\uE70E" : "\uE70D",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(changeColor)
                });
                changeStack.Children.Add(new TextBlock
                {
                    Text = $"{Math.Abs(changePercent):F0}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(changeColor),
                    Margin = new Thickness(2, 0, 0, 0)
                });
                stack.Children.Add(changeStack);
            }

            // Last week reference
            stack.Children.Add(new TextBlock
            {
                Text = $"vs {(isCurrency ? $"${lastWeek:N0}" : lastWeek.ToString())}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return stack;
        }

        private Border CreateAchievementsSection(List<Achievement> achievements)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var outerStack = new StackPanel { Spacing = 10 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE734", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0)) });
            header.Children.Add(new TextBlock
            {
                Text = "ACHIEVEMENTS",
                FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });

            var unlockedCount = achievements.Count(a => a.IsUnlocked);
            header.Children.Add(new TextBlock
            {
                Text = $"{unlockedCount}/{achievements.Count}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            outerStack.Children.Add(header);

            // Scrollable category groups
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var categoriesStack = new StackPanel { Spacing = 12 };

            // Group by category, sort by most unlocked first
            var tierRank = new Dictionary<string, int> { { "Platinum", 4 }, { "Gold", 3 }, { "Silver", 2 }, { "Bronze", 1 } };
            var groups = achievements
                .GroupBy(a => a.Category)
                .OrderByDescending(g => g.Count(a => a.IsUnlocked))
                .ThenBy(g => g.Key);

            foreach (var group in groups)
            {
                var categoryPanel = new StackPanel { Spacing = 6 };

                // Category header
                var catHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                catHeader.Children.Add(new FontIcon
                {
                    Glyph = Achievement.CategoryIcon(group.Key),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                });
                catHeader.Children.Add(new TextBlock
                {
                    Text = group.Key.ToUpperInvariant(),
                    FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    CharacterSpacing = 30
                });
                var catUnlocked = group.Count(a => a.IsUnlocked);
                catHeader.Children.Add(new TextBlock
                {
                    Text = $"{catUnlocked}/{group.Count()}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                categoryPanel.Children.Add(catHeader);

                // Sort: unlocked first (by tier desc), then locked (by % progress desc)
                var sorted = group
                    .OrderByDescending(a => a.IsUnlocked)
                    .ThenByDescending(a => a.IsUnlocked ? tierRank.GetValueOrDefault(a.Tier, 0) : 0)
                    .ThenByDescending(a => !a.IsUnlocked && a.Target > 0 ? (double)a.Progress / a.Target : 0);

                // Badge row
                var badgesWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var achievement in sorted)
                {
                    var tierColor = ColorFromHex(achievement.TierColor);
                    var badge = new Border
                    {
                        Width = 44, Height = 44,
                        CornerRadius = new CornerRadius(22),
                        Background = achievement.IsUnlocked
                            ? new SolidColorBrush(Color.FromArgb(60, tierColor.R, tierColor.G, tierColor.B))
                            : new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                        BorderBrush = achievement.IsUnlocked
                            ? new SolidColorBrush(tierColor)
                            : new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                        BorderThickness = new Thickness(2)
                    };

                    var badgeContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    badgeContent.Children.Add(new FontIcon
                    {
                        Glyph = achievement.Icon,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(achievement.IsUnlocked ? tierColor : Color.FromArgb(255, 80, 80, 80))
                    });

                    badge.Child = badgeContent;

                    var pct = achievement.Target > 0 ? (double)achievement.Progress / achievement.Target * 100 : 0;
                    var tooltipText = $"{achievement.Name} ({achievement.Tier})\n{achievement.Description}\n" +
                        (achievement.IsUnlocked ? "UNLOCKED!" : $"Progress: {achievement.Progress}/{achievement.Target} ({pct:F0}%)");
                    var toolTip = new ToolTip { Content = tooltipText };
                    ToolTipService.SetToolTip(badge, toolTip);
                    badgesWrap.Children.Add(badge);
                }

                categoryPanel.Children.Add(badgesWrap);
                categoriesStack.Children.Add(categoryPanel);
            }

            scrollViewer.Content = categoriesStack;
            outerStack.Children.Add(scrollViewer);

            // Next achievement progress bar — closest to unlock by %
            var nextAchievement = achievements
                .Where(a => !a.IsUnlocked && a.Target > 0)
                .OrderByDescending(a => (double)a.Progress / a.Target)
                .FirstOrDefault();
            if (nextAchievement != null)
            {
                var progressStack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                progressStack.Children.Add(new TextBlock
                {
                    Text = $"Next: {nextAchievement.Name}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
                });

                var progressBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                var progress = (double)nextAchievement.Progress / nextAchievement.Target * 100;
                var fill = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(AccentBlue),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(4, progress * 3) // Scale to ~300px
                };
                progressBar.Child = fill;
                progressStack.Children.Add(progressBar);

                progressStack.Children.Add(new TextBlock
                {
                    Text = $"{nextAchievement.Progress}/{nextAchievement.Target} ({progress:F0}%)",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    Margin = new Thickness(0, 2, 0, 0)
                });

                outerStack.Children.Add(progressStack);
            }

            border.Child = outerStack;
            return border;
        }

        private Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return Color.FromArgb(255,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
            return Colors.White;
        }

        private Border CreateStatCardWithTrend(string label, string value, string subtitle, Color accentColor, string glyph, double trend, double rawValue = 0, string format = "")
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { Spacing = 2 };

            // Label with icon
            var labelStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            labelStack.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = new SolidColorBrush(accentColor)
            });
            labelStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                CharacterSpacing = 40
            });
            stack.Children.Add(labelStack);

            // Value
            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            stack.Children.Add(valueBlock);
            if (rawValue > 0 && !string.IsNullOrEmpty(format))
                AnimateCountUp(valueBlock, rawValue, format);

            // Subtitle
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });

            Grid.SetColumn(stack, 0);
            mainGrid.Children.Add(stack);

            // Trend indicator
            if (Math.Abs(trend) > 0.5)
            {
                var trendStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var isUp = trend > 0;
                var trendColor = isUp ? AccentGreen : AccentRed;

                trendStack.Children.Add(new FontIcon
                {
                    Glyph = isUp ? "\uE70E" : "\uE70D",  // Up/Down arrow
                    FontSize = 14,
                    Foreground = new SolidColorBrush(trendColor)
                });
                trendStack.Children.Add(new TextBlock
                {
                    Text = $"{Math.Abs(trend):F0}%",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(trendColor),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                Grid.SetColumn(trendStack, 1);
                mainGrid.Children.Add(trendStack);
            }

            border.Child = mainGrid;
            return border;
        }

        private Border CreateTopPartsSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE8B8",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentPurple)
            });
            header.Children.Add(new TextBlock
            {
                Text = "TOP PARTS",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Get part breakdown
            var period = GetSelectedPeriod();
            var parts = _exportStats.GetPartTypeBreakdown(_currentUserId, period).Take(5).ToList();

            if (parts.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No data yet",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
            else
            {
                var maxCount = parts.Max(p => p.Count);
                foreach (var part in parts)
                {
                    var partRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    partRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    partRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    partRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Part name
                    var nameText = new TextBlock
                    {
                        Text = part.PartType,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameText, 0);
                    partRow.Children.Add(nameText);

                    // Progress bar
                    var barBg = new Border
                    {
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                        Margin = new Thickness(8, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var barFill = new Border
                    {
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(AccentPurple),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = Math.Max(8, (part.Count / (double)maxCount) * 150)
                    };
                    barBg.Child = barFill;
                    Grid.SetColumn(barBg, 1);
                    partRow.Children.Add(barBg);

                    // Count and percentage
                    var countText = new TextBlock
                    {
                        Text = $"{part.Count} ({part.Percentage:F0}%)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 60,
                        TextAlignment = TextAlignment.Right
                    };
                    Grid.SetColumn(countText, 2);
                    partRow.Children.Add(countText);

                    stack.Children.Add(partRow);
                }
            }

            border.Child = stack;
            return border;
        }

        private Border CreateStatCard(string label, string value, string subtext, Color accentColor, string icon, double rawValue = 0, string format = "")
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(0, 2, 0, 0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var stack = new StackPanel { Spacing = 4 };

            // Header with icon
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new FontIcon
            {
                Glyph = icon,
                FontSize = 10,
                Foreground = new SolidColorBrush(accentColor)
            });
            header.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            stack.Children.Add(header);

            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor)
            };
            stack.Children.Add(valueBlock);
            if (rawValue > 0 && !string.IsNullOrEmpty(format))
                AnimateCountUp(valueBlock, rawValue, format);

            stack.Children.Add(new TextBlock
            {
                Text = subtext,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });

            border.Child = stack;
            return border;
        }

        private Border CreateShopSummarySection(StatsPeriod period)
        {
            var shopStats = _exportStats.GetShopWideStats(period);

            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops = {
                        new GradientStop { Color = Color.FromArgb(255, 40, 55, 40), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 35, 45, 55), Offset = 1 }
                    }
                },
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE902",  // Building/shop icon
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentGreen)
            });
            header.Children.Add(new TextBlock
            {
                Text = "SHOP TOTALS (All Users)",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                CharacterSpacing = 40
            });
            stack.Children.Add(header);

            // Main stats row
            var statsGrid = new Grid();
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Total Value Added
            var valueStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            valueStack.Children.Add(new TextBlock
            {
                Text = shopStats.FormattedTotalValue,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentGreen),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            valueStack.Children.Add(new TextBlock
            {
                Text = "Total Value",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(valueStack, 0);
            statsGrid.Children.Add(valueStack);

            // Total Operations
            var opsStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            opsStack.Children.Add(new TextBlock
            {
                Text = shopStats.TotalOperations.ToString("N0"),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentBlue),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            opsStack.Children.Add(new TextBlock
            {
                Text = "Operations",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(opsStack, 1);
            statsGrid.Children.Add(opsStack);

            // Total Labor
            var laborStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            laborStack.Children.Add(new TextBlock
            {
                Text = shopStats.TotalLaborHours.ToString("F1"),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentPurple),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            laborStack.Children.Add(new TextBlock
            {
                Text = "Labor Hrs",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(laborStack, 2);
            statsGrid.Children.Add(laborStack);

            // Users
            var usersStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            usersStack.Children.Add(new TextBlock
            {
                Text = shopStats.TotalUsers.ToString(),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentOrange),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            usersStack.Children.Add(new TextBlock
            {
                Text = "Users",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(usersStack, 3);
            statsGrid.Children.Add(usersStack);

            stack.Children.Add(statsGrid);

            // Averages row
            var avgText = new TextBlock
            {
                Text = $"Avg per user: {shopStats.AvgOperationsPerUser:F0} ops • {shopStats.FormattedAvgValuePerUser} • {shopStats.AvgLaborPerUser:F1} hrs",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(avgText);

            border.Child = stack;
            return border;
        }

        private Border CreateTargetBreakdown(CombinedStats stats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var targets = new[]
            {
                ("CCC Desktop", stats.CCCDesktopOps, AccentBlue),
                ("CCC Web", stats.CCCWebOps, AccentPurple),
                ("Mitchell", stats.MitchellOps, AccentOrange)
            };

            for (int i = 0; i < targets.Length; i++)
            {
                var (name, count, color) = targets[i];
                var item = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                item.Children.Add(new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                item.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                Grid.SetColumn(item, i);
                grid.Children.Add(item);
            }

            border.Child = grid;
            return border;
        }

        private Border CreateMiniChart()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE9F9",
                FontSize = 12,
                Foreground = new SolidColorBrush(AccentBlue)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Last 7 Days",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(header);

            // Mini bar chart
            var dailyStats = _exportStats.GetDailyBreakdownByUser(_currentUserId).Take(7).Reverse().ToList();

            if (dailyStats.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No data for this period",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 16)
                });
            }
            else
            {
                var chartGrid = new Grid { Height = 80 };
                var maxOps = Math.Max(dailyStats.Max(d => Math.Max(d.ImportOperations, d.ExportOperations)), 1);

                var barContainer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };

                foreach (var day in dailyStats)
                {
                    var dayStack = new StackPanel { Spacing = 2, Width = 36 };

                    // Bars
                    var barsStack = new StackPanel { Height = 60 };

                    var importHeight = (day.ImportOperations / (double)maxOps) * 50;
                    var exportHeight = (day.ExportOperations / (double)maxOps) * 50;

                    var barRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
                    barRow.Children.Add(new Rectangle
                    {
                        Width = 8,
                        Height = Math.Max(importHeight, 2),
                        Fill = new SolidColorBrush(AccentGreen),
                        RadiusX = 2,
                        RadiusY = 2
                    });
                    barRow.Children.Add(new Rectangle
                    {
                        Width = 8,
                        Height = Math.Max(exportHeight, 2),
                        Fill = new SolidColorBrush(AccentBlue),
                        RadiusX = 2,
                        RadiusY = 2
                    });
                    barsStack.Children.Add(barRow);
                    dayStack.Children.Add(barsStack);

                    // Day label
                    dayStack.Children.Add(new TextBlock
                    {
                        Text = day.Date.ToString("ddd"),
                        FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    barContainer.Children.Add(dayStack);
                }

                chartGrid.Children.Add(barContainer);
                stack.Children.Add(chartGrid);

                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
                legend.Children.Add(CreateLegendItem("Import", AccentGreen));
                legend.Children.Add(CreateLegendItem("Export", AccentBlue));
                stack.Children.Add(legend);
            }

            border.Child = stack;
            return border;
        }

        #endregion

        #region Charts Tab

        private string _currentChartType = "all"; // Changed default to "all" to show everything

        private StackPanel BuildChartsTab()
        {
            var mainStack = new StackPanel { Spacing = 0 };

            // Chart selector panel at top
            mainStack.Children.Add(CreateChartSelectorPanel());

            // Stats summary row with key metrics
            mainStack.Children.Add(CreateStatsSummaryRow());

            // Charts container - shows only the selected chart
            _chartsContainer = new StackPanel { Spacing = 16, Margin = new Thickness(0, 12, 0, 0) };
            RefreshSelectedChart();
            mainStack.Children.Add(_chartsContainer);

            return mainStack;
        }

        // Chart category and item definitions
        private static readonly Dictionary<string, (string Label, Color AccentColor, (string Key, string Label)[] Charts)> ChartCategories = new()
        {
            ["bar"] = ("Bar Charts", AccentBlue, new[]
            {
                ("operations_bar", "Operations by Day"),
                ("value_bar", "Value by Day"),
                ("labor_chart", "Labor Breakdown")
            }),
            ["pie"] = ("Pie Charts", AccentPurple, new[]
            {
                ("category_pie", "Category Distribution"),
                ("operation_pie", "Operation Types")
            }),
            ["heatmap"] = ("Heatmaps", AccentOrange, new[]
            {
                ("activity_calendar", "Activity Calendar"),
                ("hourly_heatmap", "Hourly Activity"),
                ("weekday_heatmap", "Weekday Activity"),
                ("3d_heatmap", "3D Visualization"),
                ("depth_heatmap", "Depth View")
            }),
            ["trend"] = ("Trends & Stats", AccentGreen, new[]
            {
                ("operations_trend", "Operations Trend"),
                ("value_trend", "Value Trend"),
                ("avg_per_export", "Avg Per Export"),
                ("peak_hours", "Peak Hours"),
                ("streak_stats", "Streak Stats")
            })
        };

        private Border CreateChartSelectorPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Row with chart type and chart item selectors
            var selectorRow = new Grid();
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Chart Type label + combo
            var typeStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            typeStack.Children.Add(new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentBlue),
                VerticalAlignment = VerticalAlignment.Center
            });
            typeStack.Children.Add(new TextBlock
            {
                Text = "Chart Type",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _chartCategoryCombo = new ComboBox
            {
                MinWidth = 150,
                Background = new SolidColorBrush(SectionBg),
                FontSize = 12
            };
            foreach (var (key, (label, _, _)) in ChartCategories)
            {
                _chartCategoryCombo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
            }
            _chartCategoryCombo.SelectedIndex = 0;
            _chartCategoryCombo.SelectionChanged += (s, e) =>
            {
                if (_chartCategoryCombo.SelectedItem is ComboBoxItem item && item.Tag is string cat)
                {
                    _selectedChartCategory = cat;
                    PopulateChartItemCombo();
                    RefreshSelectedChart();
                }
            };
            typeStack.Children.Add(_chartCategoryCombo);
            Grid.SetColumn(typeStack, 0);
            selectorRow.Children.Add(typeStack);

            // Chart selector label + combo
            var chartStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 0, 0, 0) };
            chartStack.Children.Add(new TextBlock
            {
                Text = "Chart",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _chartItemCombo = new ComboBox
            {
                MinWidth = 200,
                Background = new SolidColorBrush(SectionBg),
                FontSize = 12
            };
            PopulateChartItemCombo();
            _chartItemCombo.SelectionChanged += ChartItemCombo_SelectionChanged;
            chartStack.Children.Add(_chartItemCombo);
            Grid.SetColumn(chartStack, 1);
            selectorRow.Children.Add(chartStack);

            mainStack.Children.Add(selectorRow);
            border.Child = mainStack;
            return border;
        }

        private void PopulateChartItemCombo()
        {
            if (_chartItemCombo == null) return;
            _chartItemCombo.SelectionChanged -= ChartItemCombo_SelectionChanged; // temporarily unhook
            _chartItemCombo.Items.Clear();

            if (ChartCategories.TryGetValue(_selectedChartCategory, out var category))
            {
                foreach (var (key, label) in category.Charts)
                {
                    _chartItemCombo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
                }
            }

            _chartItemCombo.SelectedIndex = 0;
            if (_chartItemCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string chartKey)
                _selectedChart = chartKey;

            _chartItemCombo.SelectionChanged += ChartItemCombo_SelectionChanged;
        }

        private void ChartItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_chartItemCombo?.SelectedItem is ComboBoxItem item && item.Tag is string chartKey)
            {
                _selectedChart = chartKey;
                RefreshSelectedChart();
            }
        }

        private Border CreateStatsSummaryRow()
        {
            var period = GetSelectedPeriod();
            var stats = _exportStats.GetEnhancedStats(_currentUserId, period);
            var dailyStats = _exportStats.GetDailyBreakdownByUser(_currentUserId).Take(14).ToList();
            var hourlyActivity = _exportStats.GetHourlyActivity(_currentUserId, period);

            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var grid = new Grid();
            for (int i = 0; i < 6; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Key metrics
            var totalOps = stats.TotalOperations;
            var totalValue = stats.TotalValue;
            var avgPerDay = dailyStats.Count > 0 ? dailyStats.Average(d => d.ExportOperations) : 0;
            var avgValuePerExport = stats.TotalEstimates > 0 ? totalValue / stats.TotalEstimates : 0;
            var peakHour = hourlyActivity.OrderByDescending(h => h.OperationCount).FirstOrDefault();
            var currentStreak = stats.CurrentStreak;

            // Metric cards
            var metrics = new[]
            {
                ("Total Ops", totalOps.ToString("N0"), AccentBlue, "\uE9D9"),
                ("Total Value", totalValue.ToString("C0"), AccentGreen, "\uE8C8"),
                ("Avg/Day", avgPerDay.ToString("N1"), AccentPurple, "\uE787"),
                ("Avg/Export", avgValuePerExport.ToString("C0"), AccentOrange, "\uE8EF"),
                ("Peak Hour", peakHour != null ? $"{peakHour.Hour}:00" : "N/A", AccentCyan, "\uE823"),
                ("Streak", $"{currentStreak} days", AccentRed, "\uE945")
            };

            for (int i = 0; i < metrics.Length; i++)
            {
                var (label, value, color, icon) = metrics[i];
                var card = CreateMetricMiniCard(label, value, color, icon);
                Grid.SetColumn(card, i);
                grid.Children.Add(card);
            }

            border.Child = grid;
            return border;
        }

        private Border CreateMetricMiniCard(string label, string value, Color accentColor, string glyph)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 40)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(4, 0, 4, 0)
            };

            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };

            // Icon + Label row
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            headerRow.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = new SolidColorBrush(accentColor)
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            stack.Children.Add(headerRow);

            // Value
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            border.Child = stack;
            return border;
        }

        private int CalculateCurrentStreakFromDaily(List<DailyStats> dailyStats)
        {
            if (!dailyStats.Any()) return 0;

            var sortedDays = dailyStats.OrderByDescending(d => d.Date).ToList();
            int streak = 0;
            var today = DateTime.Today;

            foreach (var day in sortedDays)
            {
                if (day.ExportOperations > 0 && (today - day.Date).Days <= streak + 1)
                {
                    streak++;
                    today = day.Date;
                }
                else if (streak == 0 && day.ExportOperations > 0)
                {
                    streak = 1;
                    today = day.Date;
                }
                else
                {
                    break;
                }
            }

            return streak;
        }

        private void RefreshSelectedChart()
        {
            if (_chartsContainer == null || _isRefreshingChart) return;
            _isRefreshingChart = true;
            try
            {

            // Save view state if this is a series toggle refresh
            (double zoom, double panX, double panY) savedState = (1.0, 0, 0);
            if (_isSeriesToggleRefresh)
                savedState = SaveChartViewState();

            _chartsContainer.Children.Clear();

            // Reset interactive chart key when switching charts
            if (!_isSeriesToggleRefresh && _currentChartKey != _selectedChart)
            {
                _currentChartKey = _selectedChart;
                _chartSeriesVisibility = GetDefaultSeriesVisibility(_selectedChart);
            }

            var dailyStats = _exportStats.GetDailyBreakdownByUser(_currentUserId).Take(14).Reverse().ToList();
            var hourlyActivity = _exportStats.GetHourlyActivity(_currentUserId, GetSelectedPeriod());
            var categoryBreakdown = _exportStats.GetCategoryBreakdown(_currentUserId, GetSelectedPeriod());
            var operationTypes = _exportStats.GetOperationTypeBreakdown(_currentUserId, GetSelectedPeriod());

            UIElement? chart = _selectedChart switch
            {
                // Bar Charts
                "operations_bar" => CreateEnhancedHorizontalBarChart("Operations by Day", dailyStats),
                "value_bar" => CreateEnhancedVerticalColumnChart("Value by Day", dailyStats),
                "labor_chart" => CreateEnhancedLaborChart(dailyStats),
                // Pie Charts
                "category_pie" => CreateEnhancedPieChart("Category Distribution", categoryBreakdown),
                "operation_pie" => CreateOperationTypePieChart("Operation Types", operationTypes),
                // Heatmaps
                "activity_calendar" => CreateGitHubStyleHeatmap("Activity Calendar", dailyStats),
                "hourly_heatmap" => CreateEnhancedHourlyHeatmap("Hourly Activity", hourlyActivity),
                "weekday_heatmap" => CreateEnhancedWeekdayHeatmap("Weekday Activity", dailyStats),
                "3d_heatmap" => Create3DHeatmapView("3D Activity Visualization", dailyStats, hourlyActivity),
                "depth_heatmap" => CreateDepthHeatmap("Depth View - Weekly Layers", dailyStats),
                // Trends & Stats
                "operations_trend" => CreateEnhancedLineChart("Operations Trend", dailyStats),
                "value_trend" => CreateEnhancedValueLineChart("Value Trend", dailyStats),
                "avg_per_export" => CreateAvgPerExportChart(dailyStats),
                "peak_hours" => CreatePeakHoursChart(hourlyActivity),
                "streak_stats" => CreateStreakStatsCard(dailyStats),
                _ => null
            };

            if (chart != null)
            {
                // Wrap all charts except 3d_heatmap (has own controls) and streak_stats (pure stats)
                if (_selectedChart != "3d_heatmap" && _selectedChart != "streak_stats" && chart is Border chartBorder)
                {
                    chart = WrapChartInteractive(chartBorder, _selectedChart);
                }
                _chartsContainer.Children.Add(chart);
            }

            // Restore view state after series toggle refresh
            if (_isSeriesToggleRefresh)
            {
                RestoreChartViewState(savedState);
                _isSeriesToggleRefresh = false;
            }
            }
            finally
            {
                _isRefreshingChart = false;
            }
        }

        // === Interactive Chart Wrapper ===

        private Border WrapChartInteractive(Border innerChart, string chartKey)
        {
            var outerStack = new StackPanel { Spacing = 0 };

            // Toolbar row
            var toolbar = BuildChartToolbar();
            outerStack.Children.Add(toolbar);

            // Series toggle panel
            _chartSeriesPanel = BuildSeriesTogglePanel(chartKey);
            if (_chartSeriesPanel.Children.Count > 0)
                outerStack.Children.Add(_chartSeriesPanel);

            // Interaction container with clip
            var clipBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Background = new SolidColorBrush(Colors.Transparent)
            };

            _interactiveChartContainer = new Grid
            {
                Background = new SolidColorBrush(Colors.Transparent),
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5)
            };

            var transformGroup = new TransformGroup();
            _chartScaleTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            _chartTranslateTransform = new TranslateTransform { X = 0, Y = 0 };
            transformGroup.Children.Add(_chartScaleTransform);
            transformGroup.Children.Add(_chartTranslateTransform);
            innerChart.RenderTransform = transformGroup;
            innerChart.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            _interactiveChartContainer.Children.Add(innerChart);

            // Wire pointer events
            _interactiveChartContainer.PointerPressed += OnChartPointerPressed;
            _interactiveChartContainer.PointerMoved += OnChartPointerMoved;
            _interactiveChartContainer.PointerReleased += OnChartPointerReleased;
            _interactiveChartContainer.PointerCaptureLost += OnChartPointerCaptureLost;
            _interactiveChartContainer.PointerWheelChanged += OnChartPointerWheelChanged;

            clipBorder.Child = _interactiveChartContainer;
            outerStack.Children.Add(clipBorder);

            _chartExpandContainer = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1),
                MaxWidth = double.PositiveInfinity,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = outerStack
            };

            // Remove border styling from inner chart since outer handles it
            innerChart.BorderThickness = new Thickness(0);
            innerChart.CornerRadius = new CornerRadius(0, 0, 10, 10);

            return _chartExpandContainer;
        }

        private StackPanel BuildChartToolbar()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25))
            };

            // Zoom out
            var zoomOutBtn = Create3DControlButton("\uE71F", "Zoom Out");
            zoomOutBtn.Click += (s, e) => AdjustChartZoom(-0.2);
            bar.Children.Add(zoomOutBtn);

            // Zoom label
            _chartZoomLabel = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 40,
                TextAlignment = TextAlignment.Center
            };
            bar.Children.Add(_chartZoomLabel);

            // Zoom in
            var zoomInBtn = Create3DControlButton("\uE710", "Zoom In");
            zoomInBtn.Click += (s, e) => AdjustChartZoom(0.2);
            bar.Children.Add(zoomInBtn);

            bar.Children.Add(new Border { Width = 12 }); // Spacer

            // Expand/Shrink toggle
            var expandBtn = Create3DControlButton("\uE740", "Expand / Shrink");
            expandBtn.Click += (s, e) => ToggleChartExpand();
            bar.Children.Add(expandBtn);

            bar.Children.Add(new Border { Width = 12 }); // Spacer

            // Reset
            var resetBtn = Create3DControlButton("\uE72C", "Reset View");
            resetBtn.Click += (s, e) => ResetChartView();
            bar.Children.Add(resetBtn);

            // Hint text
            bar.Children.Add(new Border { Width = 12 });
            bar.Children.Add(new TextBlock
            {
                Text = "Scroll to zoom \u2022 Drag to pan",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return bar;
        }

        private StackPanel BuildSeriesTogglePanel(string chartKey)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30))
            };

            var seriesMap = GetDefaultSeriesVisibility(chartKey);
            if (seriesMap.Count == 0) return panel;

            // Series color mapping
            var colorMap = new Dictionary<string, Color>
            {
                { "Body Labor", AccentBlue },
                { "Refinish", AccentPurple },
                { "Export Ops", AccentBlue },
                { "Import Ops", AccentGreen },
                { "Export Value", AccentGreen },
                { "Import Value", AccentOrange },
                { "Operations", AccentCyan },
                { "Exports", AccentOrange },
                { "Learned", Color.FromArgb(255, 255, 200, 50) }
            };

            foreach (var kvp in _chartSeriesVisibility)
            {
                var seriesName = kvp.Key;
                var isVisible = kvp.Value;
                var color = colorMap.ContainsKey(seriesName) ? colorMap[seriesName] : AccentBlue;

                var cb = new CheckBox
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = seriesName, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)) }
                        }
                    },
                    IsChecked = isVisible,
                    MinWidth = 0,
                    Padding = new Thickness(4, 2, 4, 2)
                };

                var capturedName = seriesName;
                cb.Checked += (s, e) => { _chartSeriesVisibility[capturedName] = true; _isSeriesToggleRefresh = true; RefreshSelectedChart(); };
                cb.Unchecked += (s, e) => { _chartSeriesVisibility[capturedName] = false; _isSeriesToggleRefresh = true; RefreshSelectedChart(); };
                panel.Children.Add(cb);
            }

            return panel;
        }

        private Dictionary<string, bool> GetDefaultSeriesVisibility(string chartKey)
        {
            return chartKey switch
            {
                "labor_chart" => new Dictionary<string, bool> { { "Body Labor", true }, { "Refinish", true } },
                "operations_trend" => new Dictionary<string, bool> { { "Export Ops", true }, { "Import Ops", false }, { "Learned", false } },
                "value_trend" => new Dictionary<string, bool> { { "Export Value", true }, { "Import Value", false } },
                "peak_hours" => new Dictionary<string, bool> { { "Operations", true }, { "Exports", false }, { "Learned", false } },
                _ => new Dictionary<string, bool>()
            };
        }

        // === Chart Pointer Interaction Handlers ===

        private void OnChartPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(_interactiveChartContainer);
            var delta = point.Properties.MouseWheelDelta;
            AdjustChartZoom(delta > 0 ? 0.15 : -0.15);
            e.Handled = true;
        }

        private void OnChartPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_interactiveChartContainer == null) return;
            _chartIsDragging = true;
            _chartLastPointerPosition = e.GetCurrentPoint(_interactiveChartContainer).Position;
            _interactiveChartContainer.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnChartPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_chartIsDragging || _interactiveChartContainer == null || _chartTranslateTransform == null) return;

            var currentPos = e.GetCurrentPoint(_interactiveChartContainer).Position;
            var deltaX = currentPos.X - _chartLastPointerPosition.X;
            var deltaY = currentPos.Y - _chartLastPointerPosition.Y;

            _chartPanX += deltaX;
            _chartPanY += deltaY;

            var maxPan = 300 * _chartZoomLevel;
            _chartPanX = Math.Clamp(_chartPanX, -maxPan, maxPan);
            _chartPanY = Math.Clamp(_chartPanY, -maxPan, maxPan);

            _chartTranslateTransform.X = _chartPanX;
            _chartTranslateTransform.Y = _chartPanY;

            _chartLastPointerPosition = currentPos;
            e.Handled = true;
        }

        private void OnChartPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _chartIsDragging = false;
            if (_interactiveChartContainer != null)
                _interactiveChartContainer.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void OnChartPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _chartIsDragging = false;
        }

        private void AdjustChartZoom(double delta)
        {
            _chartZoomLevel = Math.Clamp(_chartZoomLevel + delta, 0.5, 4.0);
            if (_chartScaleTransform != null)
            {
                _chartScaleTransform.ScaleX = _chartZoomLevel;
                _chartScaleTransform.ScaleY = _chartZoomLevel;
            }
            if (_chartZoomLabel != null)
                _chartZoomLabel.Text = $"{(int)(_chartZoomLevel * 100)}%";
        }

        private void ResetChartView()
        {
            _chartZoomLevel = 1.0;
            _chartPanX = 0;
            _chartPanY = 0;

            if (_chartScaleTransform != null)
            {
                _chartScaleTransform.ScaleX = 1;
                _chartScaleTransform.ScaleY = 1;
            }
            if (_chartTranslateTransform != null)
            {
                _chartTranslateTransform.X = 0;
                _chartTranslateTransform.Y = 0;
            }
            if (_chartZoomLabel != null)
                _chartZoomLabel.Text = "100%";
        }

        private void ToggleChartExpand()
        {
            _chartIsExpanded = !_chartIsExpanded;
            if (_chartExpandContainer != null)
            {
                if (_chartIsExpanded)
                {
                    _chartExpandContainer.MaxWidth = double.PositiveInfinity;
                    _chartExpandContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else
                {
                    _chartExpandContainer.MaxWidth = 620;
                    _chartExpandContainer.HorizontalAlignment = HorizontalAlignment.Center;
                }
            }
        }

        private (double zoom, double panX, double panY) SaveChartViewState()
        {
            return (_chartZoomLevel, _chartPanX, _chartPanY);
        }

        private void RestoreChartViewState((double zoom, double panX, double panY) state)
        {
            _chartZoomLevel = state.zoom;
            _chartPanX = state.panX;
            _chartPanY = state.panY;

            if (_chartScaleTransform != null)
            {
                _chartScaleTransform.ScaleX = _chartZoomLevel;
                _chartScaleTransform.ScaleY = _chartZoomLevel;
            }
            if (_chartTranslateTransform != null)
            {
                _chartTranslateTransform.X = _chartPanX;
                _chartTranslateTransform.Y = _chartPanY;
            }
            if (_chartZoomLabel != null)
                _chartZoomLabel.Text = $"{(int)(_chartZoomLevel * 100)}%";
        }

        // Enhanced Horizontal Bar Chart with animated bars
        private Border CreateEnhancedHorizontalBarChart(string title, List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            var totalOps = data.Sum(d => d.ExportOperations);
            var avgOps = data.Count > 0 ? data.Average(d => d.ExportOperations) : 0;
            var summaryText = new TextBlock { Text = $"Total: {totalOps} | Avg: {avgOps:N1}/day", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(summaryText, 1);
            header.Children.Add(summaryText);
            mainStack.Children.Add(header);

            if (!data.Any())
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var items = data.TakeLast(10).ToList();
                var maxCount = items.Max(d => d.ExportOperations);
                if (maxCount == 0) maxCount = 1;

                for (int idx = 0; idx < items.Count; idx++)
                {
                    var day = items[idx];
                    var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

                    row.Children.Add(new TextBlock { Text = day.Date.ToString("MMM dd"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)), VerticalAlignment = VerticalAlignment.Center });

                    var targetWidth = (double)day.ExportOperations / maxCount * 380;
                    var barContainer = new Grid { HorizontalAlignment = HorizontalAlignment.Left };

                    // Track background
                    barContainer.Children.Add(new Border { Width = 380, Height = 22, Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)), CornerRadius = new CornerRadius(4) });

                    // Animated foreground bar
                    var intensity = (double)day.ExportOperations / maxCount;
                    var bar = new Border
                    {
                        Width = 0,
                        Height = 22,
                        CornerRadius = new CornerRadius(4),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0, 0),
                            EndPoint = new Windows.Foundation.Point(1, 0),
                            GradientStops = { new GradientStop { Color = InterpolateColor(AccentBlue, AccentCyan, intensity), Offset = 0 }, new GradientStop { Color = AccentBlue, Offset = 1 } }
                        }
                    };
                    barContainer.Children.Add(bar);
                    AnimateBarWidth(bar, Math.Max(targetWidth, 2), delayMs: idx * 50);

                    Grid.SetColumn(barContainer, 1);
                    row.Children.Add(barContainer);

                    var countText = new TextBlock { Text = day.ExportOperations.ToString(), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(countText, 2);
                    row.Children.Add(countText);

                    mainStack.Children.Add(row);
                }
            }

            border.Child = mainStack;
            return border;
        }

        private Color InterpolateColor(Color from, Color to, double t)
        {
            return Color.FromArgb(
                (byte)(from.A + (to.A - from.A) * t),
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t)
            );
        }

        // Enhanced Vertical Column Chart with axes and gridlines
        private Border CreateEnhancedVerticalColumnChart(string title, List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            var totalValue = data.Sum(d => d.ExportPrice);
            var summaryText = new TextBlock { Text = $"Total: {totalValue:C0}", FontSize = 11, Foreground = new SolidColorBrush(AccentGreen), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(summaryText, 1);
            header.Children.Add(summaryText);
            mainStack.Children.Add(header);

            if (!data.Any() || data.All(d => d.ExportPrice == 0))
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var items = data.TakeLast(10).ToList();
                const double cW = 900, cH = 400, pL = 65, pR = 880, pT = 20, pB = 350;
                var plotW = pR - pL;
                var plotH = pB - pT;
                var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                var maxVal = (double)Math.Max(items.Max(d => d.ExportPrice), 1);
                DrawChartAxes(canvas, pL, pT, pR, pB, maxVal, "C0");

                double slotW = plotW / items.Count;
                double colW = slotW * 0.65;
                double gap = slotW * 0.35;

                for (int i = 0; i < items.Count; i++)
                {
                    var x = pL + i * slotW + gap / 2;
                    var targetH = (double)items[i].ExportPrice / maxVal * plotH;
                    var intensity = (double)items[i].ExportPrice / maxVal;

                    // Column container for bottom-aligned growth
                    var colContainer = new Grid { Width = colW, Height = plotH };
                    Canvas.SetLeft(colContainer, x);
                    Canvas.SetTop(colContainer, pT);

                    var col = new Border
                    {
                        Width = colW,
                        Height = Math.Max(targetH, 2),
                        CornerRadius = new CornerRadius(3, 3, 0, 0),
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 1),
                            EndPoint = new Windows.Foundation.Point(0.5, 0),
                            GradientStops = { new GradientStop { Color = AccentGreen, Offset = 0 }, new GradientStop { Color = InterpolateColor(AccentGreen, AccentCyan, intensity), Offset = 1 } }
                        }
                    };
                    colContainer.Children.Add(col);
                    canvas.Children.Add(colContainer);
                    AnimateColumnHeight(col, Math.Max(targetH, 2), delayMs: i * 40);

                    // Value label above column
                    if (items[i].ExportPrice > 0)
                    {
                        var valLabel = new TextBlock { Text = items[i].ExportPrice.ToString("C0"), FontSize = 8, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), TextAlignment = TextAlignment.Center, Width = colW + 8 };
                        Canvas.SetLeft(valLabel, x - 4);
                        Canvas.SetTop(valLabel, pB - targetH - 14);
                        canvas.Children.Add(valLabel);
                    }

                    // Date label
                    var dateLabel = new TextBlock { Text = items[i].Date.ToString("MMM dd"), FontSize = 8, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), TextAlignment = TextAlignment.Center, Width = slotW };
                    Canvas.SetLeft(dateLabel, pL + i * slotW);
                    Canvas.SetTop(dateLabel, pB + 6);
                    canvas.Children.Add(dateLabel);
                }

                mainStack.Children.Add(canvas);
            }

            border.Child = mainStack;
            return border;
        }

        // Enhanced Labor Chart - stacked daily bar chart
        private Border CreateEnhancedLaborChart(List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            var totalLabor = data.Sum(d => d.ExportLabor);
            var totalRefinish = data.Sum(d => d.ExportPaint);
            var total = totalLabor + totalRefinish;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = "Labor Hours Breakdown", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            var totalLabel = new TextBlock { Text = $"Total: {total:N1} hrs", FontSize = 11, Foreground = new SolidColorBrush(AccentBlue), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(totalLabel, 1);
            header.Children.Add(totalLabel);
            mainStack.Children.Add(header);

            if (total == 0)
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var items = data.Where(d => d.ExportLabor > 0 || d.ExportPaint > 0).TakeLast(10).ToList();
                if (items.Count == 0) items = data.TakeLast(10).ToList();

                const double cW = 900, cH = 400, pL = 65, pR = 880, pT = 20, pB = 350;
                var plotW = pR - pL;
                var plotH = pB - pT;
                var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                var showLabor = !_chartSeriesVisibility.ContainsKey("Body Labor") || _chartSeriesVisibility["Body Labor"];
                var showRefinish = !_chartSeriesVisibility.ContainsKey("Refinish") || _chartSeriesVisibility["Refinish"];

                var maxVal = (double)Math.Max(items.Max(d =>
                    (showLabor ? d.ExportLabor : 0) + (showRefinish ? d.ExportPaint : 0)), 1);
                DrawChartAxes(canvas, pL, pT, pR, pB, maxVal, "N1");

                double slotW = plotW / items.Count;
                double colW = slotW * 0.65;
                double gap = slotW * 0.35;

                for (int i = 0; i < items.Count; i++)
                {
                    var x = pL + i * slotW + gap / 2;
                    var laborH = showLabor ? (double)items[i].ExportLabor / maxVal * plotH : 0;
                    var refinishH = showRefinish ? (double)items[i].ExportPaint / maxVal * plotH : 0;

                    // Stacked columns: refinish on top of labor
                    var colContainer = new Grid { Width = colW, Height = plotH };
                    Canvas.SetLeft(colContainer, x);
                    Canvas.SetTop(colContainer, pT);

                    // Combined stack panel aligned to bottom
                    var colStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

                    // Refinish (top)
                    if (showRefinish && refinishH > 0)
                    {
                        colStack.Children.Add(new Border
                        {
                            Width = colW,
                            Height = Math.Max(refinishH, 1),
                            CornerRadius = laborH > 0 ? new CornerRadius(3, 3, 0, 0) : new CornerRadius(3),
                            Background = new SolidColorBrush(AccentPurple)
                        });
                    }

                    // Labor (bottom)
                    if (showLabor && laborH > 0)
                    {
                        colStack.Children.Add(new Border
                        {
                            Width = colW,
                            Height = Math.Max(laborH, 1),
                            CornerRadius = refinishH > 0 ? new CornerRadius(0) : new CornerRadius(3, 3, 0, 0),
                            Background = new SolidColorBrush(AccentBlue)
                        });
                    }

                    colContainer.Children.Add(colStack);
                    canvas.Children.Add(colContainer);

                    // Date label
                    var dateLabel = new TextBlock { Text = items[i].Date.ToString("MMM dd"), FontSize = 8, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), TextAlignment = TextAlignment.Center, Width = slotW };
                    Canvas.SetLeft(dateLabel, pL + i * slotW);
                    Canvas.SetTop(dateLabel, pB + 6);
                    canvas.Children.Add(dateLabel);
                }

                mainStack.Children.Add(canvas);

                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
                legend.Children.Add(CreateLaborLegendItem("Body Labor", totalLabor, AccentBlue, total > 0 ? (double)(totalLabor / total * 100) : 0));
                legend.Children.Add(CreateLaborLegendItem("Refinish", totalRefinish, AccentPurple, total > 0 ? (double)(totalRefinish / total * 100) : 0));
                mainStack.Children.Add(legend);
            }

            border.Child = mainStack;
            return border;
        }

        private StackPanel CreateLaborLegendItem(string label, decimal hours, Color color, double percentage)
        {
            var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color)
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(row);

            stack.Children.Add(new TextBlock
            {
                Text = $"{hours:N1} hrs ({percentage:N0}%)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return stack;
        }

        // Enhanced Pie Chart
        private Border CreateEnhancedPieChart(string title, CategoryBreakdown breakdown)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel { Spacing = 16 };
            mainStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (breakdown.TotalDollars == 0)
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var chartGrid = new Grid();
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Pie chart canvas
                var pieCanvas = new Canvas { Width = 260, Height = 260 };
                var categories = new[]
                {
                    (breakdown.LaborPercent, AccentBlue, "Labor", breakdown.LaborDollars),
                    (breakdown.RefinishPercent, AccentPurple, "Refinish", breakdown.RefinishDollars),
                    (breakdown.MaterialsPercent, AccentOrange, "Materials", breakdown.MaterialsDollars),
                    (breakdown.OtherPercent, Color.FromArgb(255, 100, 100, 100), "Other", breakdown.OtherDollars)
                };

                double startAngle = -90; // Start from top
                foreach (var (percent, color, name, value) in categories)
                {
                    if (percent <= 0) continue;
                    var segment = CreatePieSegment(130, 130, 115, startAngle, percent * 3.6, color);
                    pieCanvas.Children.Add(segment);
                    startAngle += percent * 3.6;
                }

                // Center donut hole
                var centerCircle = new Ellipse
                {
                    Width = 110,
                    Height = 110,
                    Fill = new SolidColorBrush(CardBg)
                };
                Canvas.SetLeft(centerCircle, 75);
                Canvas.SetTop(centerCircle, 75);
                pieCanvas.Children.Add(centerCircle);

                // Total in center
                var totalStack = new StackPanel { Spacing = 0 };
                totalStack.Children.Add(new TextBlock
                {
                    Text = "Total",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                totalStack.Children.Add(new TextBlock
                {
                    Text = breakdown.TotalDollars.ToString("C0"),
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var totalBorder = new Border { Child = totalStack };
                Canvas.SetLeft(totalBorder, 90);
                Canvas.SetTop(totalBorder, 110);
                pieCanvas.Children.Add(totalBorder);

                Grid.SetColumn(pieCanvas, 0);
                chartGrid.Children.Add(pieCanvas);

                // Legend with values
                var legend = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                foreach (var (percent, color, name, value) in categories)
                {
                    if (percent <= 0) continue;
                    var legendItem = new Grid();
                    legendItem.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    legendItem.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    legendItem.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var colorBox = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(color),
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(colorBox, 0);
                    legendItem.Children.Add(colorBox);

                    var nameText = new TextBlock
                    {
                        Text = name,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameText, 1);
                    legendItem.Children.Add(nameText);

                    var valueStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                    valueStack.Children.Add(new TextBlock
                    {
                        Text = value.ToString("C0"),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(color),
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                    valueStack.Children.Add(new TextBlock
                    {
                        Text = $"{percent:N1}%",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                    Grid.SetColumn(valueStack, 2);
                    legendItem.Children.Add(valueStack);

                    legend.Children.Add(legendItem);
                }
                Grid.SetColumn(legend, 1);
                chartGrid.Children.Add(legend);

                mainStack.Children.Add(chartGrid);
            }

            border.Child = mainStack;
            return border;
        }

        // Enhanced Line Chart with axes and gridlines
        private Border CreateEnhancedLineChart(string title, List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            // Header with trend
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });

            if (data.Count >= 2)
            {
                var recentAvg = data.TakeLast(3).Average(d => d.ExportOperations);
                var olderAvg = data.Take(Math.Max(1, data.Count - 3)).Average(d => d.ExportOperations);
                var trend = olderAvg > 0 ? ((recentAvg - olderAvg) / olderAvg * 100) : 0;
                var trendColor = trend >= 0 ? AccentGreen : AccentRed;
                var trendStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                trendStack.Children.Add(new FontIcon { Glyph = trend >= 0 ? "\uE70E" : "\uE70D", FontSize = 12, Foreground = new SolidColorBrush(trendColor) });
                trendStack.Children.Add(new TextBlock { Text = $"{Math.Abs(trend):N1}%", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(trendColor) });
                Grid.SetColumn(trendStack, 1);
                header.Children.Add(trendStack);
            }
            mainStack.Children.Add(header);

            if (!data.Any())
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                const double cW = 900, cH = 380, pL = 65, pR = 880, pT = 20, pB = 330;
                var plotW = pR - pL;
                var plotH = pB - pT;
                var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                var showExport = !_chartSeriesVisibility.ContainsKey("Export Ops") || _chartSeriesVisibility["Export Ops"];
                var showImport = _chartSeriesVisibility.ContainsKey("Import Ops") && _chartSeriesVisibility["Import Ops"];
                var showLearned = _chartSeriesVisibility.ContainsKey("Learned") && _chartSeriesVisibility["Learned"];

                var maxVal = (double)Math.Max(
                    Math.Max(Math.Max(showExport ? data.Max(d => d.ExportOperations) : 0,
                             showImport ? data.Max(d => d.ImportOperations) : 0),
                             showLearned ? data.Max(d => d.LearnOperations) : 0), 1);
                DrawChartAxes(canvas, pL, pT, pR, pB, maxVal, "N0");

                double xStep = plotW / Math.Max(data.Count - 1, 1);

                // Export Ops line
                if (showExport)
                {
                    var points = new List<Windows.Foundation.Point>();
                    for (int i = 0; i < data.Count; i++)
                        points.Add(new Windows.Foundation.Point(pL + i * xStep, pB - (data[i].ExportOperations / maxVal * plotH)));

                    if (points.Count > 1)
                    {
                        var area = new Polygon();
                        var pts = new PointCollection();
                        pts.Add(new Windows.Foundation.Point(points[0].X, pB));
                        foreach (var pt in points) pts.Add(pt);
                        pts.Add(new Windows.Foundation.Point(points[^1].X, pB));
                        area.Points = pts;
                        area.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0),
                            EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(60, 0, 120, 215), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 0, 120, 215), Offset = 1 } }
                        };
                        canvas.Children.Add(area);

                        var polyline = new Polyline { StrokeThickness = 2.5, Stroke = new SolidColorBrush(AccentBlue), StrokeLineJoin = PenLineJoin.Round };
                        foreach (var pt in points) polyline.Points.Add(pt);
                        canvas.Children.Add(polyline);
                    }

                    foreach (var pt in points)
                    {
                        var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(AccentBlue), StrokeThickness = 2 };
                        Canvas.SetLeft(dot, pt.X - 4);
                        Canvas.SetTop(dot, pt.Y - 4);
                        canvas.Children.Add(dot);
                    }
                }

                // Import Ops line
                if (showImport)
                {
                    var importPoints = new List<Windows.Foundation.Point>();
                    for (int i = 0; i < data.Count; i++)
                        importPoints.Add(new Windows.Foundation.Point(pL + i * xStep, pB - (data[i].ImportOperations / maxVal * plotH)));

                    if (importPoints.Count > 1)
                    {
                        var importArea = new Polygon();
                        var iPts = new PointCollection();
                        iPts.Add(new Windows.Foundation.Point(importPoints[0].X, pB));
                        foreach (var pt in importPoints) iPts.Add(pt);
                        iPts.Add(new Windows.Foundation.Point(importPoints[^1].X, pB));
                        importArea.Points = iPts;
                        importArea.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0),
                            EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(40, 0, 150, 80), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 0, 150, 80), Offset = 1 } }
                        };
                        canvas.Children.Add(importArea);

                        var importLine = new Polyline { StrokeThickness = 2.5, Stroke = new SolidColorBrush(AccentGreen), StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 4, 2 } };
                        foreach (var pt in importPoints) importLine.Points.Add(pt);
                        canvas.Children.Add(importLine);
                    }

                    foreach (var pt in importPoints)
                    {
                        var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(AccentGreen), StrokeThickness = 2 };
                        Canvas.SetLeft(dot, pt.X - 3.5);
                        Canvas.SetTop(dot, pt.Y - 3.5);
                        canvas.Children.Add(dot);
                    }
                }

                // Learned line
                var learnedColor = Color.FromArgb(255, 255, 200, 50);
                if (showLearned)
                {
                    var learnPoints = new List<Windows.Foundation.Point>();
                    for (int i = 0; i < data.Count; i++)
                        learnPoints.Add(new Windows.Foundation.Point(pL + i * xStep, pB - (data[i].LearnOperations / maxVal * plotH)));

                    if (learnPoints.Count > 1)
                    {
                        var learnArea = new Polygon();
                        var lPts = new PointCollection();
                        lPts.Add(new Windows.Foundation.Point(learnPoints[0].X, pB));
                        foreach (var pt in learnPoints) lPts.Add(pt);
                        lPts.Add(new Windows.Foundation.Point(learnPoints[^1].X, pB));
                        learnArea.Points = lPts;
                        learnArea.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0),
                            EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(40, 255, 200, 50), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 255, 200, 50), Offset = 1 } }
                        };
                        canvas.Children.Add(learnArea);

                        var learnLine = new Polyline { StrokeThickness = 2.5, Stroke = new SolidColorBrush(learnedColor), StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 6, 3 } };
                        foreach (var pt in learnPoints) learnLine.Points.Add(pt);
                        canvas.Children.Add(learnLine);
                    }

                    foreach (var pt in learnPoints)
                    {
                        var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(learnedColor), StrokeThickness = 2 };
                        Canvas.SetLeft(dot, pt.X - 3.5);
                        Canvas.SetTop(dot, pt.Y - 3.5);
                        canvas.Children.Add(dot);
                    }
                }

                DrawChartDateLabels(canvas, data, pL, pB, xStep);
                mainStack.Children.Add(canvas);
            }

            border.Child = mainStack;
            return border;
        }

        // Enhanced Value Line Chart with axes and gridlines
        private Border CreateEnhancedValueLineChart(string title, List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            var totalValue = data.Sum(d => d.ExportPrice);
            var summaryText = new TextBlock { Text = $"Total: {totalValue:C0}", FontSize = 11, Foreground = new SolidColorBrush(AccentGreen), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(summaryText, 1);
            header.Children.Add(summaryText);
            mainStack.Children.Add(header);

            if (!data.Any() || data.All(d => d.ExportPrice == 0))
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                const double cW = 900, cH = 380, pL = 65, pR = 880, pT = 20, pB = 330;
                var plotW = pR - pL;
                var plotH = pB - pT;
                var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                var showExportVal = !_chartSeriesVisibility.ContainsKey("Export Value") || _chartSeriesVisibility["Export Value"];
                var showImportVal = _chartSeriesVisibility.ContainsKey("Import Value") && _chartSeriesVisibility["Import Value"];

                var maxVal = (double)Math.Max(
                    Math.Max(showExportVal ? data.Max(d => d.ExportPrice) : 0,
                             showImportVal ? data.Max(d => d.ImportPrice) : 0), 1);
                DrawChartAxes(canvas, pL, pT, pR, pB, maxVal, "C0");

                double xStep = plotW / Math.Max(data.Count - 1, 1);

                // Export Value line
                if (showExportVal)
                {
                    var points = new List<Windows.Foundation.Point>();
                    for (int i = 0; i < data.Count; i++)
                        points.Add(new Windows.Foundation.Point(pL + i * xStep, pB - ((double)data[i].ExportPrice / maxVal * plotH)));

                    if (points.Count > 1)
                    {
                        var area = new Polygon();
                        var pts = new PointCollection();
                        pts.Add(new Windows.Foundation.Point(points[0].X, pB));
                        foreach (var pt in points) pts.Add(pt);
                        pts.Add(new Windows.Foundation.Point(points[^1].X, pB));
                        area.Points = pts;
                        area.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0),
                            EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(60, 0, 150, 80), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 0, 150, 80), Offset = 1 } }
                        };
                        canvas.Children.Add(area);

                        var polyline = new Polyline { StrokeThickness = 2.5, Stroke = new SolidColorBrush(AccentGreen), StrokeLineJoin = PenLineJoin.Round };
                        foreach (var pt in points) polyline.Points.Add(pt);
                        canvas.Children.Add(polyline);
                    }

                    foreach (var pt in points)
                    {
                        var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(AccentGreen), StrokeThickness = 2 };
                        Canvas.SetLeft(dot, pt.X - 4);
                        Canvas.SetTop(dot, pt.Y - 4);
                        canvas.Children.Add(dot);
                    }
                }

                // Import Value line
                if (showImportVal)
                {
                    var importPoints = new List<Windows.Foundation.Point>();
                    for (int i = 0; i < data.Count; i++)
                        importPoints.Add(new Windows.Foundation.Point(pL + i * xStep, pB - ((double)data[i].ImportPrice / maxVal * plotH)));

                    if (importPoints.Count > 1)
                    {
                        var importArea = new Polygon();
                        var iPts = new PointCollection();
                        iPts.Add(new Windows.Foundation.Point(importPoints[0].X, pB));
                        foreach (var pt in importPoints) iPts.Add(pt);
                        iPts.Add(new Windows.Foundation.Point(importPoints[^1].X, pB));
                        importArea.Points = iPts;
                        importArea.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0),
                            EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(40, 255, 150, 0), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 255, 150, 0), Offset = 1 } }
                        };
                        canvas.Children.Add(importArea);

                        var importLine = new Polyline { StrokeThickness = 2.5, Stroke = new SolidColorBrush(AccentOrange), StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 4, 2 } };
                        foreach (var pt in importPoints) importLine.Points.Add(pt);
                        canvas.Children.Add(importLine);
                    }

                    foreach (var pt in importPoints)
                    {
                        var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(AccentOrange), StrokeThickness = 2 };
                        Canvas.SetLeft(dot, pt.X - 3.5);
                        Canvas.SetTop(dot, pt.Y - 3.5);
                        canvas.Children.Add(dot);
                    }
                }

                DrawChartDateLabels(canvas, data, pL, pB, xStep);
                mainStack.Children.Add(canvas);
            }

            border.Child = mainStack;
            return border;
        }

        // Average Per Export Chart with daily value line
        private Border CreateAvgPerExportChart(List<DailyStats> data)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(new TextBlock { Text = "Average Per Export", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });

            if (!data.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var totalOps = data.Sum(d => d.ExportOperations);
                var totalExports = data.Count(d => d.ExportCount > 0);
                var totalValue = data.Sum(d => d.ExportPrice);
                var totalLabor = data.Sum(d => d.ExportLabor);
                var avgOps = totalExports > 0 ? (double)totalOps / totalExports : 0;
                var avgValue = totalExports > 0 ? totalValue / totalExports : 0;
                var avgLabor = totalExports > 0 ? totalLabor / totalExports : 0;

                // Summary cards row
                var grid = new Grid();
                for (int i = 0; i < 4; i++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var metrics = new[] { ("Avg Ops", avgOps.ToString("N1"), AccentBlue), ("Avg Value", avgValue.ToString("C0"), AccentGreen), ("Avg Labor", $"{avgLabor:N1} hrs", AccentPurple), ("Active Days", totalExports.ToString(), AccentOrange) };
                for (int i = 0; i < metrics.Length; i++)
                {
                    var (label, value, color) = metrics[i];
                    var card = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 45)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(4, 0, 4, 0) };
                    var cardStack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
                    cardStack.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center });
                    cardStack.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)), HorizontalAlignment = HorizontalAlignment.Center });
                    card.Child = cardStack;
                    Grid.SetColumn(card, i);
                    grid.Children.Add(card);
                }
                stack.Children.Add(grid);

                // Daily avg value line chart
                var activeDays = data.Where(d => d.ExportCount > 0).ToList();
                if (activeDays.Count >= 2)
                {
                    stack.Children.Add(new TextBlock { Text = "Daily Average Value", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), Margin = new Thickness(0, 8, 0, 0) });

                    const double cW = 900, cH = 300, pL = 65, pR = 880, pT = 15, pB = 255;
                    var plotW = pR - pL;
                    var plotH = pB - pT;
                    var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                    var dailyAvgs = activeDays.Select(d => d.ExportCount > 0 ? (double)(d.ExportPrice / d.ExportCount) : 0).ToList();
                    var maxAvg = Math.Max(dailyAvgs.Max(), 1);
                    DrawChartAxes(canvas, pL, pT, pR, pB, maxAvg, "C0", 3);

                    var points = new List<Windows.Foundation.Point>();
                    double xStep = plotW / Math.Max(activeDays.Count - 1, 1);
                    for (int i = 0; i < activeDays.Count; i++)
                        points.Add(new Windows.Foundation.Point(pL + i * xStep, pB - (dailyAvgs[i] / maxAvg * plotH)));

                    // Area fill
                    if (points.Count > 1)
                    {
                        var area = new Polygon();
                        var pts = new PointCollection();
                        pts.Add(new Windows.Foundation.Point(points[0].X, pB));
                        foreach (var pt in points) pts.Add(pt);
                        pts.Add(new Windows.Foundation.Point(points[^1].X, pB));
                        area.Points = pts;
                        area.Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1),
                            GradientStops = { new GradientStop { Color = Color.FromArgb(50, 255, 150, 0), Offset = 0 }, new GradientStop { Color = Color.FromArgb(5, 255, 150, 0), Offset = 1 } }
                        };
                        canvas.Children.Add(area);
                    }

                    if (points.Count > 1)
                    {
                        var polyline = new Polyline { StrokeThickness = 2, Stroke = new SolidColorBrush(AccentOrange), StrokeLineJoin = PenLineJoin.Round };
                        foreach (var pt in points) polyline.Points.Add(pt);
                        canvas.Children.Add(polyline);
                    }

                    foreach (var pt in points)
                    {
                        var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(CardBg), Stroke = new SolidColorBrush(AccentOrange), StrokeThickness = 1.5 };
                        Canvas.SetLeft(dot, pt.X - 3);
                        Canvas.SetTop(dot, pt.Y - 3);
                        canvas.Children.Add(dot);
                    }

                    DrawChartDateLabels(canvas, activeDays, pL, pB, xStep);
                    stack.Children.Add(canvas);
                }
            }

            border.Child = stack;
            return border;
        }

        // Peak Hours Chart - full 24-hour histogram
        private Border CreatePeakHoursChart(List<HourlyActivity> hourlyData)
        {
            var border = new Border { Background = new SolidColorBrush(CardBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), BorderThickness = new Thickness(1) };
            var mainStack = new StackPanel { Spacing = 12 };

            var peakHour = hourlyData.OrderByDescending(h => h.OperationCount).FirstOrDefault();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = "Peak Hours Analysis", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) });
            if (peakHour != null && peakHour.OperationCount > 0)
            {
                var peakText = new TextBlock { Text = $"Peak: {peakHour.FormattedHour}", FontSize = 11, Foreground = new SolidColorBrush(AccentCyan), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(peakText, 1);
                header.Children.Add(peakText);
            }
            mainStack.Children.Add(header);

            if (!hourlyData.Any() || hourlyData.All(h => h.OperationCount == 0))
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var showOps = !_chartSeriesVisibility.ContainsKey("Operations") || _chartSeriesVisibility["Operations"];
                var showExports = _chartSeriesVisibility.ContainsKey("Exports") && _chartSeriesVisibility["Exports"];
                var showLearnedPeak = _chartSeriesVisibility.ContainsKey("Learned") && _chartSeriesVisibility["Learned"];

                // Build full 24-hour arrays
                var allHours = new int[24];
                var allExports = new int[24];
                var allLearns = new int[24];
                foreach (var h in hourlyData)
                {
                    if (h.Hour >= 0 && h.Hour < 24)
                    {
                        allHours[h.Hour] = h.OperationCount;
                        allExports[h.Hour] = h.ExportCount;
                        allLearns[h.Hour] = h.LearnCount;
                    }
                }

                // Find range with activity (pad 1 hour each side)
                int firstHour = Array.FindIndex(allHours, c => c > 0);
                int lastHour = Array.FindLastIndex(allHours, c => c > 0);
                if (firstHour < 0) { firstHour = 8; lastHour = 17; }
                firstHour = Math.Max(0, firstHour - 1);
                lastHour = Math.Min(23, lastHour + 1);
                int hourCount = lastHour - firstHour + 1;
                if (hourCount < 6) { firstHour = Math.Max(0, firstHour - 2); lastHour = Math.Min(23, lastHour + 2); hourCount = lastHour - firstHour + 1; }

                const double cW = 900, cH = 380, pL = 65, pR = 880, pT = 20, pB = 330;
                var plotW = pR - pL;
                var plotH = pB - pT;
                var canvas = new Canvas { Width = cW, Height = cH, HorizontalAlignment = HorizontalAlignment.Center };

                var maxVal = (double)Math.Max(
                    Math.Max(Math.Max(showOps ? allHours.Max() : 0, showExports ? allExports.Max() : 0),
                             showLearnedPeak ? allLearns.Max() : 0), 1);
                DrawChartAxes(canvas, pL, pT, pR, pB, maxVal, "N0");

                // Calculate bar widths based on how many series are visible
                int visibleSeries = (showOps ? 1 : 0) + (showExports ? 1 : 0) + (showLearnedPeak ? 1 : 0);
                double slotW = plotW / hourCount;
                double colW = slotW * 0.7;
                double gap = slotW * 0.3;
                double seriesW = visibleSeries > 1 ? colW / visibleSeries : colW;
                int peakIdx = peakHour?.Hour ?? -1;

                for (int i = 0; i < hourCount; i++)
                {
                    int hour = firstHour + i;
                    var x = pL + i * slotW + gap / 2;
                    int seriesIdx = 0;

                    // Operations bars (main)
                    if (showOps)
                    {
                        var targetH = allHours[hour] / maxVal * plotH;
                        var intensity = allHours[hour] / maxVal;
                        bool isPeak = hour == peakIdx;

                        var colContainer = new Grid { Width = seriesW, Height = plotH };
                        Canvas.SetLeft(colContainer, x + seriesIdx * seriesW);
                        Canvas.SetTop(colContainer, pT);

                        var col = new Border
                        {
                            Width = seriesW,
                            Height = Math.Max(targetH, allHours[hour] > 0 ? 2 : 0),
                            CornerRadius = new CornerRadius(2, 2, 0, 0),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Background = new LinearGradientBrush
                            {
                                StartPoint = new Windows.Foundation.Point(0.5, 1),
                                EndPoint = new Windows.Foundation.Point(0.5, 0),
                                GradientStops = { new GradientStop { Color = AccentCyan, Offset = 0 }, new GradientStop { Color = InterpolateColor(AccentCyan, isPeak ? AccentOrange : AccentPurple, intensity), Offset = 1 } }
                            }
                        };
                        colContainer.Children.Add(col);
                        canvas.Children.Add(colContainer);
                        if (targetH > 0) AnimateColumnHeight(col, Math.Max(targetH, 2), delayMs: i * 25);

                        if (allHours[hour] > 0 && targetH > 20)
                        {
                            var valLabel = new TextBlock { Text = allHours[hour].ToString(), FontSize = 8, Foreground = new SolidColorBrush(Colors.White), TextAlignment = TextAlignment.Center, Width = seriesW };
                            Canvas.SetLeft(valLabel, x + seriesIdx * seriesW);
                            Canvas.SetTop(valLabel, pB - targetH - 13);
                            canvas.Children.Add(valLabel);
                        }
                        seriesIdx++;
                    }

                    // Exports overlay bars
                    if (showExports)
                    {
                        var exportH = allExports[hour] / maxVal * plotH;

                        var exportContainer = new Grid { Width = seriesW, Height = plotH };
                        Canvas.SetLeft(exportContainer, x + seriesIdx * seriesW);
                        Canvas.SetTop(exportContainer, pT);

                        var exportCol = new Border
                        {
                            Width = seriesW,
                            Height = Math.Max(exportH, allExports[hour] > 0 ? 2 : 0),
                            CornerRadius = new CornerRadius(2, 2, 0, 0),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Background = new SolidColorBrush(Color.FromArgb(200, 255, 150, 0))
                        };
                        exportContainer.Children.Add(exportCol);
                        canvas.Children.Add(exportContainer);
                        seriesIdx++;
                    }

                    // Learned bars
                    if (showLearnedPeak)
                    {
                        var learnH = allLearns[hour] / maxVal * plotH;

                        var learnContainer = new Grid { Width = seriesW, Height = plotH };
                        Canvas.SetLeft(learnContainer, x + seriesIdx * seriesW);
                        Canvas.SetTop(learnContainer, pT);

                        var learnCol = new Border
                        {
                            Width = seriesW,
                            Height = Math.Max(learnH, allLearns[hour] > 0 ? 2 : 0),
                            CornerRadius = new CornerRadius(2, 2, 0, 0),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Background = new SolidColorBrush(Color.FromArgb(200, 255, 200, 50))
                        };
                        learnContainer.Children.Add(learnCol);
                        canvas.Children.Add(learnContainer);
                        seriesIdx++;
                    }

                    // Hour label
                    var hourStr = hour == 0 ? "12a" : hour < 12 ? $"{hour}a" : hour == 12 ? "12p" : $"{hour - 12}p";
                    var hourLabel = new TextBlock { Text = hourStr, FontSize = 8, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), TextAlignment = TextAlignment.Center, Width = slotW };
                    Canvas.SetLeft(hourLabel, pL + i * slotW);
                    Canvas.SetTop(hourLabel, pB + 6);
                    canvas.Children.Add(hourLabel);
                }

                mainStack.Children.Add(canvas);
            }

            border.Child = mainStack;
            return border;
        }

        // New: Streak Stats Card
        private Border CreateStreakStatsCard(List<DailyStats> dailyStats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(new TextBlock
            {
                Text = "🔥 Activity Streaks",
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (!dailyStats.Any())
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var currentStreak = CalculateCurrentStreakFromDaily(dailyStats);
                var longestStreak = CalculateLongestStreak(dailyStats);
                var activeDays = dailyStats.Count(d => d.ExportOperations > 0);
                var totalDays = dailyStats.Count;
                var consistency = totalDays > 0 ? (double)activeDays / totalDays * 100 : 0;

                var grid = new Grid();
                for (int i = 0; i < 4; i++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var metrics = new[]
                {
                    ("Current", $"{currentStreak} days", AccentRed, "\uE945"),
                    ("Longest", $"{longestStreak} days", AccentOrange, "\uE734"),
                    ("Active Days", activeDays.ToString(), AccentGreen, "\uE787"),
                    ("Consistency", $"{consistency:N0}%", AccentBlue, "\uE9D9")
                };

                for (int i = 0; i < metrics.Length; i++)
                {
                    var (label, value, color, glyph) = metrics[i];
                    var card = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 45)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(4, 0, 4, 0)
                    };

                    var cardStack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };

                    // Icon
                    cardStack.Children.Add(new FontIcon
                    {
                        Glyph = glyph,
                        FontSize = 18,
                        Foreground = new SolidColorBrush(color)
                    });

                    cardStack.Children.Add(new TextBlock
                    {
                        Text = value,
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    cardStack.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    card.Child = cardStack;
                    Grid.SetColumn(card, i);
                    grid.Children.Add(card);
                }

                stack.Children.Add(grid);
            }

            border.Child = stack;
            return border;
        }

        private int CalculateLongestStreak(List<DailyStats> dailyStats)
        {
            if (!dailyStats.Any()) return 0;

            var sortedDays = dailyStats.OrderBy(d => d.Date).ToList();
            int longest = 0;
            int current = 0;
            DateTime? lastDate = null;

            foreach (var day in sortedDays)
            {
                if (day.ExportOperations > 0)
                {
                    if (lastDate.HasValue && (day.Date - lastDate.Value).Days == 1)
                    {
                        current++;
                    }
                    else
                    {
                        current = 1;
                    }
                    lastDate = day.Date;
                    longest = Math.Max(longest, current);
                }
                else
                {
                    current = 0;
                    lastDate = null;
                }
            }

            return longest;
        }

        private Border CreateChartTypeSelector()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            // Label
            stack.Children.Add(new TextBlock
            {
                Text = "Visualization Type:",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Dropdown ComboBox
            var comboBox = new ComboBox
            {
                Width = 220,
                Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var chartTypes = new[]
            {
                ("bar", "Bar Charts", "\uE9D9"),
                ("pie", "Pie Charts", "\uE9F9"),
                ("heatmap", "Heatmap (Activity Grid)", "\uE8C6"),
                ("heatmap3d", "3D Heatmap (Depth View)", "\uF158"),
                ("line", "Line Charts (Trends)", "\uE8B5")
            };

            int selectedIndex = 0;
            for (int i = 0; i < chartTypes.Length; i++)
            {
                var (id, label, icon) = chartTypes[i];

                var itemStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                itemStack.Children.Add(new FontIcon
                {
                    Glyph = icon,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(AccentBlue)
                });
                itemStack.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var item = new ComboBoxItem
                {
                    Content = itemStack,
                    Tag = id
                };
                comboBox.Items.Add(item);

                if (id == _currentChartType) selectedIndex = i;
            }

            comboBox.SelectedIndex = selectedIndex;

            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string chartType)
                {
                    _currentChartType = chartType;
                    RefreshStats();
                }
            };

            stack.Children.Add(comboBox);

            border.Child = stack;
            return border;
        }

        private Border CreatePieChart(string title, CategoryBreakdown breakdown)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var mainStack = new StackPanel { Spacing = 12 };
            mainStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (breakdown.TotalDollars == 0)
            {
                mainStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var chartGrid = new Grid();
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Pie chart visual (using concentric circles to simulate pie)
                var pieCanvas = new Canvas { Width = 260, Height = 260 };
                var categories = new[]
                {
                    (breakdown.LaborPercent, AccentBlue, "Labor"),
                    (breakdown.RefinishPercent, AccentPurple, "Refinish"),
                    (breakdown.MaterialsPercent, AccentOrange, "Materials"),
                    (breakdown.OtherPercent, Color.FromArgb(255, 100, 100, 100), "Other")
                };

                double startAngle = 0;
                foreach (var (percent, color, name) in categories)
                {
                    if (percent <= 0) continue;
                    var segment = CreatePieSegment(130, 130, 115, startAngle, percent * 3.6, color);
                    pieCanvas.Children.Add(segment);
                    startAngle += percent * 3.6;
                }

                // Center circle (donut effect)
                var center = new Ellipse
                {
                    Width = 110, Height = 110,
                    Fill = new SolidColorBrush(CardBg)
                };
                Canvas.SetLeft(center, 75);
                Canvas.SetTop(center, 75);
                pieCanvas.Children.Add(center);

                // Total in center
                var totalText = new TextBlock
                {
                    Text = breakdown.TotalDollars.ToString("C0"),
                    FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                Canvas.SetLeft(totalText, 95);
                Canvas.SetTop(totalText, 115);
                pieCanvas.Children.Add(totalText);

                Grid.SetColumn(pieCanvas, 0);
                chartGrid.Children.Add(pieCanvas);

                // Legend
                var legend = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                foreach (var (percent, color, name) in categories)
                {
                    if (percent <= 0) continue;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new Border
                    {
                        Width = 12, Height = 12,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(color)
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{name}: {percent:F1}%",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White)
                    });
                    legend.Children.Add(row);
                }
                Grid.SetColumn(legend, 1);
                chartGrid.Children.Add(legend);

                mainStack.Children.Add(chartGrid);
            }

            border.Child = mainStack;
            return border;
        }

        private Microsoft.UI.Xaml.Shapes.Path CreatePieSegment(double cx, double cy, double radius, double startAngle, double sweepAngle, Color color)
        {
            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var x1 = cx + radius * Math.Cos(startRad);
            var y1 = cy + radius * Math.Sin(startRad);
            var x2 = cx + radius * Math.Cos(endRad);
            var y2 = cy + radius * Math.Sin(endRad);

            var largeArc = sweepAngle > 180 ? 1 : 0;

            var pathData = $"M {cx},{cy} L {x1},{y1} A {radius},{radius} 0 {largeArc},1 {x2},{y2} Z";

            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = ParsePath(pathData),
                Fill = new SolidColorBrush(color)
            };
        }

        private Microsoft.UI.Xaml.Media.Geometry ParsePath(string data)
        {
            // Simplified path for pie segments
            var geometry = new PathGeometry();
            var figure = new PathFigure();

            var parts = data.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "M" && i + 2 < parts.Length)
                {
                    var coords = parts[i + 1].Split(',');
                    figure.StartPoint = new Windows.Foundation.Point(double.Parse(coords[0]), double.Parse(coords[1]));
                }
            }

            geometry.Figures.Add(figure);
            return geometry;
        }

        private Border CreateOperationTypePieChart(string title, List<OperationTypeStats> operations)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (operations.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var total = operations.Sum(o => o.Count);
                var colors = new[] { AccentBlue, AccentGreen, AccentPurple, AccentOrange, AccentCyan, AccentRed };

                var chartStack = new StackPanel { Spacing = 6 };
                int colorIndex = 0;
                foreach (var op in operations.Take(6))
                {
                    var percent = total > 0 ? (double)op.Count / total * 100 : 0;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    row.Children.Add(new TextBlock
                    {
                        Text = op.OperationType,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White)
                    });

                    var bar = new Border
                    {
                        Height = 16,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                        Margin = new Thickness(8, 0, 8, 0)
                    };
                    var fill = new Border
                    {
                        Height = 16,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(colors[colorIndex % colors.Length]),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = Math.Max(4, percent * 2)
                    };
                    bar.Child = fill;
                    Grid.SetColumn(bar, 1);
                    row.Children.Add(bar);

                    var countText = new TextBlock
                    {
                        Text = $"{op.Count} ({percent:F0}%)",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                        MinWidth = 60,
                        TextAlignment = TextAlignment.Right
                    };
                    Grid.SetColumn(countText, 2);
                    row.Children.Add(countText);

                    chartStack.Children.Add(row);
                    colorIndex++;
                }
                stack.Children.Add(chartStack);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateHourlyHeatmap(string title, List<HourlyActivity> hourlyData)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "When are you most productive?",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });

            // Heatmap grid (24 hours)
            var heatmapGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };

            // Create 24 columns
            for (int i = 0; i < 24; i++)
            {
                heatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            heatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heatmapGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var maxOps = hourlyData.Max(h => h.OperationCount);
            if (maxOps == 0) maxOps = 1;

            for (int i = 0; i < 24; i++)
            {
                var hourData = hourlyData.FirstOrDefault(h => h.Hour == i) ?? new HourlyActivity { Hour = i };
                var intensity = (double)hourData.OperationCount / maxOps;

                // Heat cell
                var cell = new Border
                {
                    Height = 30,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(GetHeatColor(intensity))
                };
                ToolTipService.SetToolTip(cell, $"{hourData.FormattedHour}: {hourData.OperationCount} operations");
                Grid.SetColumn(cell, i);
                Grid.SetRow(cell, 1);
                heatmapGrid.Children.Add(cell);

                // Hour label (every 4 hours)
                if (i % 4 == 0)
                {
                    var label = new TextBlock
                    {
                        Text = hourData.FormattedHour.Replace(" AM", "a").Replace(" PM", "p"),
                        FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    Grid.SetColumn(label, i);
                    Grid.SetRow(label, 0);
                    heatmapGrid.Children.Add(label);
                }
            }

            stack.Children.Add(heatmapGrid);

            // Legend
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            legend.Children.Add(new TextBlock { Text = "Less", FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)) });
            for (int i = 0; i <= 4; i++)
            {
                legend.Children.Add(new Border
                {
                    Width = 12, Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(GetHeatColor(i / 4.0))
                });
            }
            legend.Children.Add(new TextBlock { Text = "More", FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)) });
            stack.Children.Add(legend);

            border.Child = stack;
            return border;
        }

        private Color GetHeatColor(double intensity)
        {
            // From dark to bright green
            var baseR = (byte)(30 + intensity * 30);
            var baseG = (byte)(40 + intensity * 180);
            var baseB = (byte)(30 + intensity * 60);
            return Color.FromArgb(255, baseR, baseG, baseB);
        }

        private Border CreateWeekdayHeatmap(string title, List<DailyStats> dailyStats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Group by day of week
            var weekdayGroups = new Dictionary<DayOfWeek, int>();
            foreach (var day in dailyStats)
            {
                var dow = day.Date.DayOfWeek;
                if (!weekdayGroups.ContainsKey(dow)) weekdayGroups[dow] = 0;
                weekdayGroups[dow] += day.ExportOperations;
            }

            var maxOps = weekdayGroups.Values.DefaultIfEmpty(1).Max();
            if (maxOps == 0) maxOps = 1;

            var days = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                              DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
            var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

            var heatmapGrid = new Grid();
            for (int i = 0; i < 7; i++)
            {
                heatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            heatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heatmapGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            heatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < 7; i++)
            {
                var dow = days[i];
                var ops = weekdayGroups.ContainsKey(dow) ? weekdayGroups[dow] : 0;
                var intensity = (double)ops / maxOps;

                // Day label
                var label = new TextBlock
                {
                    Text = dayNames[i],
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(label, i);
                Grid.SetRow(label, 0);
                heatmapGrid.Children.Add(label);

                // Heat cell
                var cell = new Border
                {
                    Height = 40,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(GetHeatColor(intensity))
                };
                Grid.SetColumn(cell, i);
                Grid.SetRow(cell, 1);
                heatmapGrid.Children.Add(cell);

                // Count label
                var countLabel = new TextBlock
                {
                    Text = ops.ToString(),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(countLabel, i);
                Grid.SetRow(countLabel, 2);
                heatmapGrid.Children.Add(countLabel);
            }

            stack.Children.Add(heatmapGrid);

            border.Child = stack;
            return border;
        }

        // ========== ENHANCED HEATMAP VISUALIZATIONS ==========

        private Border CreateGitHubStyleHeatmap(string title, List<DailyStats> dailyStats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 16 };

            // Title with glow effect
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE8C6",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 57, 211, 83))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(titleStack);

            stack.Children.Add(new TextBlock
            {
                Text = "Contribution-style activity calendar",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, -8, 0, 0)
            });

            // Build calendar grid (7 rows x 12 columns for ~3 months)
            var calendarGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };

            // 12 weeks + 1 for day labels
            for (int i = 0; i <= 12; i++)
                calendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });

            // 7 days
            for (int i = 0; i < 7; i++)
                calendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Day labels
            var dayLabels = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int i = 0; i < 7; i++)
            {
                var dayLabel = new TextBlock
                {
                    Text = dayLabels[i],
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(dayLabel, 0);
                Grid.SetRow(dayLabel, i);
                calendarGrid.Children.Add(dayLabel);
            }

            // Build lookup from daily stats
            var lookup = dailyStats.ToDictionary(d => d.Date.Date, d => d.ExportOperations);
            var maxOps = dailyStats.Count > 0 ? dailyStats.Max(d => d.ExportOperations) : 1;
            if (maxOps == 0) maxOps = 1;

            // Fill calendar cells (last 12 weeks)
            var today = DateTime.Today;
            var startDate = today.AddDays(-(int)today.DayOfWeek - 77); // Start from 11 weeks ago, aligned to Sunday

            for (int week = 0; week < 12; week++)
            {
                for (int day = 0; day < 7; day++)
                {
                    var cellDate = startDate.AddDays(week * 7 + day);
                    var ops = lookup.ContainsKey(cellDate) ? lookup[cellDate] : 0;
                    var intensity = (double)ops / maxOps;

                    var cell = new Border
                    {
                        Width = 18,
                        Height = 18,
                        Margin = new Thickness(2),
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(GetGitHubHeatColor(intensity))
                    };

                    // Add glow effect for high activity
                    if (intensity > 0.7)
                    {
                        cell.BorderBrush = new SolidColorBrush(Color.FromArgb(100, 57, 211, 83));
                        cell.BorderThickness = new Thickness(1);
                    }

                    ToolTipService.SetToolTip(cell, $"{cellDate:MMM dd, yyyy}: {ops} operations");
                    Grid.SetColumn(cell, week + 1);
                    Grid.SetRow(cell, day);
                    calendarGrid.Children.Add(cell);
                }
            }

            stack.Children.Add(calendarGrid);

            // Enhanced legend
            var legendStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            legendStack.Children.Add(new TextBlock { Text = "Less", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });

            for (int i = 0; i <= 4; i++)
            {
                var legendCell = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(GetGitHubHeatColor(i / 4.0))
                };
                legendStack.Children.Add(legendCell);
            }

            legendStack.Children.Add(new TextBlock { Text = "More", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(legendStack);

            border.Child = stack;
            return border;
        }

        private Color GetGitHubHeatColor(double intensity)
        {
            // GitHub-style green gradient
            if (intensity <= 0) return Color.FromArgb(255, 22, 27, 34); // Empty
            if (intensity <= 0.25) return Color.FromArgb(255, 14, 68, 41); // Light
            if (intensity <= 0.5) return Color.FromArgb(255, 0, 109, 50); // Medium
            if (intensity <= 0.75) return Color.FromArgb(255, 38, 166, 65); // High
            return Color.FromArgb(255, 57, 211, 83); // Max
        }

        private Border CreateEnhancedHourlyHeatmap(string title, List<HourlyActivity> hourlyData)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 16 };

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE823",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(titleStack);

            stack.Children.Add(new TextBlock
            {
                Text = "Peak productivity hours",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, -8, 0, 0)
            });

            // 24-hour circular-style heatmap
            var heatmapContainer = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 80 };

            var maxOps = hourlyData.Count > 0 ? hourlyData.Max(h => h.OperationCount) : 1;
            if (maxOps == 0) maxOps = 1;

            // Time periods
            var periods = new[] { ("Night", 0, 6), ("Morning", 6, 12), ("Afternoon", 12, 18), ("Evening", 18, 24) };
            var periodColors = new[]
            {
                Color.FromArgb(255, 63, 81, 181),  // Night - Indigo
                Color.FromArgb(255, 255, 193, 7),  // Morning - Amber
                Color.FromArgb(255, 76, 175, 80),  // Afternoon - Green
                Color.FromArgb(255, 156, 39, 176)  // Evening - Purple
            };

            var periodGrid = new Grid();
            for (int p = 0; p < 4; p++)
                periodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int p = 0; p < 4; p++)
            {
                var (periodName, startHour, endHour) = periods[p];
                var periodOps = hourlyData.Where(h => h.Hour >= startHour && h.Hour < endHour).Sum(h => h.OperationCount);
                var periodMax = hourlyData.Where(h => h.Hour >= startHour && h.Hour < endHour).Select(h => h.OperationCount).DefaultIfEmpty(0).Max();
                var intensity = maxOps > 0 ? (double)periodMax / maxOps : 0;

                var periodCard = new Border
                {
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(80 + intensity * 175), periodColors[p].R, periodColors[p].G, periodColors[p].B)),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                // Add gradient glow for high activity
                if (intensity > 0.5)
                {
                    periodCard.BorderBrush = new SolidColorBrush(Color.FromArgb(150, periodColors[p].R, periodColors[p].G, periodColors[p].B));
                    periodCard.BorderThickness = new Thickness(2);
                }

                var cardContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                cardContent.Children.Add(new TextBlock
                {
                    Text = periodName,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                cardContent.Children.Add(new TextBlock
                {
                    Text = $"{periodOps} ops",
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                cardContent.Children.Add(new TextBlock
                {
                    Text = $"{startHour}:00-{endHour}:00",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                periodCard.Child = cardContent;
                Grid.SetColumn(periodCard, p);
                periodGrid.Children.Add(periodCard);
            }

            stack.Children.Add(periodGrid);

            // Detailed hour bars
            var hourGrid = new Grid { Margin = new Thickness(0, 12, 0, 0), Height = 40 };
            for (int i = 0; i < 24; i++)
                hourGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < 24; i++)
            {
                var hourData = hourlyData.FirstOrDefault(h => h.Hour == i) ?? new HourlyActivity { Hour = i };
                var intensity = (double)hourData.OperationCount / maxOps;

                // Determine period color
                int periodIdx = i < 6 ? 0 : i < 12 ? 1 : i < 18 ? 2 : 3;
                var baseColor = periodColors[periodIdx];

                var bar = new Border
                {
                    Height = 8 + intensity * 32,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(1, 0, 1, 0),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(100 + intensity * 155), baseColor.R, baseColor.G, baseColor.B))
                };

                ToolTipService.SetToolTip(bar, $"{hourData.FormattedHour}: {hourData.OperationCount} operations");
                Grid.SetColumn(bar, i);
                hourGrid.Children.Add(bar);
            }

            stack.Children.Add(hourGrid);

            // Hour labels
            var labelGrid = new Grid();
            for (int i = 0; i < 6; i++)
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var hourLabels = new[] { "12am", "4am", "8am", "12pm", "4pm", "8pm" };
            for (int i = 0; i < 6; i++)
            {
                var lbl = new TextBlock
                {
                    Text = hourLabels[i],
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(lbl, i);
                labelGrid.Children.Add(lbl);
            }
            stack.Children.Add(labelGrid);

            border.Child = stack;
            return border;
        }

        private Border CreateEnhancedWeekdayHeatmap(string title, List<DailyStats> dailyStats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 16 };

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE787",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 188, 212))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(titleStack);

            // Group by day of week
            var weekdayGroups = new Dictionary<DayOfWeek, (int ops, decimal value)>();
            foreach (var day in dailyStats)
            {
                var dow = day.Date.DayOfWeek;
                if (!weekdayGroups.ContainsKey(dow)) weekdayGroups[dow] = (0, 0);
                var current = weekdayGroups[dow];
                weekdayGroups[dow] = (current.ops + day.ExportOperations, current.value + day.ExportPrice);
            }

            var maxOps = weekdayGroups.Values.Select(v => v.ops).DefaultIfEmpty(1).Max();
            if (maxOps == 0) maxOps = 1;

            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                              DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var fullDayNames = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

            // Circular/radial style cards
            var dayGrid = new Grid();
            for (int i = 0; i < 7; i++)
                dayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < 7; i++)
            {
                var dow = days[i];
                var (ops, value) = weekdayGroups.ContainsKey(dow) ? weekdayGroups[dow] : (0, 0);
                var intensity = (double)ops / maxOps;

                // Determine if weekend
                bool isWeekend = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
                var baseColor = isWeekend ? Color.FromArgb(255, 156, 39, 176) : Color.FromArgb(255, 0, 188, 212);

                var dayCard = new Border
                {
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(60 + intensity * 195), baseColor.R, baseColor.G, baseColor.B)),
                    Padding = new Thickness(6, 12, 6, 12)
                };

                if (intensity > 0.6)
                {
                    dayCard.BorderBrush = new SolidColorBrush(Color.FromArgb(180, baseColor.R, baseColor.G, baseColor.B));
                    dayCard.BorderThickness = new Thickness(2);
                }

                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
                content.Children.Add(new TextBlock
                {
                    Text = dayNames[i],
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                content.Children.Add(new TextBlock
                {
                    Text = ops.ToString(),
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                content.Children.Add(new TextBlock
                {
                    Text = "ops",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, -4, 0, 0)
                });

                dayCard.Child = content;
                ToolTipService.SetToolTip(dayCard, $"{fullDayNames[i]}: {ops} operations, ${value:N0} value");
                Grid.SetColumn(dayCard, i);
                dayGrid.Children.Add(dayCard);
            }

            stack.Children.Add(dayGrid);

            border.Child = stack;
            return border;
        }

        // ========== 3D HEATMAP VISUALIZATIONS ==========

        private Border Create3DHeatmapView(string title, List<DailyStats> dailyStats, List<HourlyActivity> hourlyActivity)
        {
            // Cache the data for rotation redraws
            _cached3DHourlyActivity = hourlyActivity;

            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 16 };

            // Title with 3D icon
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uF158",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 233, 30, 99))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(titleStack);

            stack.Children.Add(new TextBlock
            {
                Text = "Interactive 3D view - Drag to pan, scroll to zoom, use controls to rotate",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, -8, 0, 0)
            });

            // === CONTROL BAR ===
            var controlBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

            // Zoom controls
            var zoomOutBtn = Create3DControlButton("\uE71F", "Zoom Out");
            zoomOutBtn.Click += (s, e) => Adjust3DZoom(-0.2);
            controlBar.Children.Add(zoomOutBtn);

            _3dZoomLabel = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 40,
                TextAlignment = TextAlignment.Center
            };
            controlBar.Children.Add(_3dZoomLabel);

            var zoomInBtn = Create3DControlButton("\uE710", "Zoom In");
            zoomInBtn.Click += (s, e) => Adjust3DZoom(0.2);
            controlBar.Children.Add(zoomInBtn);

            controlBar.Children.Add(new Border { Width = 16 }); // Spacer

            // Rotation controls
            var rotateLeftBtn = Create3DControlButton("\uE76B", "Rotate Left");
            rotateLeftBtn.Click += (s, e) => Adjust3DRotation(-45);
            controlBar.Children.Add(rotateLeftBtn);

            var rotateRightBtn = Create3DControlButton("\uE76C", "Rotate Right");
            rotateRightBtn.Click += (s, e) => Adjust3DRotation(45);
            controlBar.Children.Add(rotateRightBtn);

            controlBar.Children.Add(new Border { Width = 16 }); // Spacer

            // Reset button
            var resetBtn = Create3DControlButton("\uE72C", "Reset View");
            resetBtn.Click += (s, e) => Reset3DView();
            controlBar.Children.Add(resetBtn);

            stack.Children.Add(controlBar);

            // === INTERACTIVE CANVAS CONTAINER ===
            var canvasHeight = 450;
            var canvasWidth = 900;

            // Clip container to prevent overflow
            var clipContainer = new Border
            {
                Width = canvasWidth,
                Height = canvasHeight,
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 22)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0)
            };

            // Container grid with transforms
            _3dHeatmapContainer = new Grid
            {
                Width = canvasWidth,
                Height = canvasHeight,
                Background = new SolidColorBrush(Colors.Transparent)
            };

            // Set up transforms
            var transformGroup = new TransformGroup();
            _3dScaleTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1, CenterX = canvasWidth / 2, CenterY = canvasHeight / 2 };
            _3dTranslateTransform = new TranslateTransform { X = 0, Y = 0 };
            transformGroup.Children.Add(_3dScaleTransform);
            transformGroup.Children.Add(_3dTranslateTransform);

            // Create and add canvas
            _3dHeatmapCanvas = new Canvas { Width = canvasWidth, Height = canvasHeight };
            _3dHeatmapCanvas.RenderTransform = transformGroup;

            // Draw the 3D bars
            Draw3DHeatmapBars(hourlyActivity, canvasWidth, canvasHeight);

            _3dHeatmapContainer.Children.Add(_3dHeatmapCanvas);

            // Wire up pointer events for pan
            _3dHeatmapContainer.PointerPressed += On3DPointerPressed;
            _3dHeatmapContainer.PointerMoved += On3DPointerMoved;
            _3dHeatmapContainer.PointerReleased += On3DPointerReleased;
            _3dHeatmapContainer.PointerCaptureLost += On3DPointerCaptureLost;
            _3dHeatmapContainer.PointerWheelChanged += On3DPointerWheelChanged;

            clipContainer.Child = _3dHeatmapContainer;
            stack.Children.Add(clipContainer);

            // Legend
            var legendStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            legendStack.Children.Add(new TextBlock { Text = "Low Activity", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });

            for (int i = 0; i <= 5; i++)
            {
                var legendCell = new Border
                {
                    Width = 20,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Get3DHeatColor(i / 5.0))
                };
                legendStack.Children.Add(legendCell);
            }

            legendStack.Children.Add(new TextBlock { Text = "High Activity", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(legendStack);

            // Instructions
            var instructionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 4, 0, 0) };
            instructionsStack.Children.Add(CreateControlHint("\uE8B0", "Drag to pan"));
            instructionsStack.Children.Add(CreateControlHint("\uE8B3", "Scroll to zoom"));
            instructionsStack.Children.Add(CreateControlHint("\uE7AD", "Buttons to rotate"));
            stack.Children.Add(instructionsStack);

            border.Child = stack;
            return border;
        }

        private Button Create3DControlButton(string glyph, string tooltip)
        {
            var btn = new Button
            {
                Content = new FontIcon { Glyph = glyph, FontSize = 14 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
                CornerRadius = new CornerRadius(6)
            };
            ToolTipService.SetToolTip(btn, tooltip);
            return btn;
        }

        private StackPanel CreateControlHint(string glyph, string text)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)) });
            stack.Children.Add(new TextBlock { Text = text, FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)) });
            return stack;
        }

        private void Draw3DHeatmapBars(List<HourlyActivity> hourlyActivity, int canvasWidth, int canvasHeight)
        {
            if (_3dHeatmapCanvas == null) return;
            _3dHeatmapCanvas.Children.Clear();

            // Build 7x24 grid (days x hours)
            var lookup = new double[7, 24];
            double maxVal = 1;

            foreach (var hour in hourlyActivity)
            {
                for (int d = 0; d < 7; d++)
                {
                    lookup[d, hour.Hour] = hour.OperationCount * (1 + d * 0.1);
                    if (lookup[d, hour.Hour] > maxVal) maxVal = lookup[d, hour.Hour];
                }
            }

            // Rotation-adjusted isometric projection
            var rotRad = _3dRotationAngle * Math.PI / 180;
            var cosRot = Math.Cos(rotRad);
            var sinRot = Math.Sin(rotRad);

            var cellWidth = 18;
            var cellHeight = 14;
            var maxBarHeight = 80;
            var offsetX = canvasWidth / 2;
            var offsetY = 60;

            // Determine draw order based on rotation
            var dayOrder = new List<int>();
            var hourOrder = new List<int>();
            for (int i = 0; i < 7; i++) dayOrder.Add(i);
            for (int i = 0; i < 24; i++) hourOrder.Add(i);

            // Sort by depth based on rotation angle
            var rotQuadrant = (int)((_3dRotationAngle % 360 + 360) % 360 / 90);
            if (rotQuadrant == 1 || rotQuadrant == 2) dayOrder.Reverse();
            if (rotQuadrant == 2 || rotQuadrant == 3) hourOrder.Reverse();

            foreach (int d in dayOrder)
            {
                foreach (int h in hourOrder)
                {
                    var intensity = lookup[d, h] / maxVal;
                    var barHeight = 8 + intensity * maxBarHeight;

                    // Base position before rotation
                    var baseX = (h - 12) * cellWidth * 0.5;
                    var baseY = (d - 3) * cellHeight * 0.5;

                    // Apply rotation
                    var rotX = baseX * cosRot - baseY * sinRot;
                    var rotY = baseX * sinRot + baseY * cosRot;

                    // Isometric projection
                    var isoX = offsetX + rotX;
                    var isoY = offsetY + rotY * 0.6 + canvasHeight / 2 - 80;

                    var color = Get3DHeatColor(intensity);

                    var barGroup = new Canvas();

                    // Adjust face visibility based on rotation
                    var showFront = cosRot >= 0;
                    var showRight = sinRot >= 0;

                    // Front face
                    if (showFront)
                    {
                        var frontFace = new Microsoft.UI.Xaml.Shapes.Polygon
                        {
                            Points = new PointCollection
                            {
                                new Windows.Foundation.Point(0, barHeight),
                                new Windows.Foundation.Point(cellWidth * 0.5, barHeight + cellHeight * 0.3),
                                new Windows.Foundation.Point(cellWidth * 0.5, cellHeight * 0.3),
                                new Windows.Foundation.Point(0, 0)
                            },
                            Fill = new SolidColorBrush(color)
                        };
                        barGroup.Children.Add(frontFace);
                    }
                    else
                    {
                        // Back face when rotated
                        var backFace = new Microsoft.UI.Xaml.Shapes.Polygon
                        {
                            Points = new PointCollection
                            {
                                new Windows.Foundation.Point(cellWidth * 0.5, barHeight + cellHeight * 0.3),
                                new Windows.Foundation.Point(cellWidth, barHeight),
                                new Windows.Foundation.Point(cellWidth, 0),
                                new Windows.Foundation.Point(cellWidth * 0.5, cellHeight * 0.3)
                            },
                            Fill = new SolidColorBrush(color)
                        };
                        barGroup.Children.Add(backFace);
                    }

                    // Right/Left face
                    if (showRight)
                    {
                        var rightFace = new Microsoft.UI.Xaml.Shapes.Polygon
                        {
                            Points = new PointCollection
                            {
                                new Windows.Foundation.Point(cellWidth * 0.5, barHeight + cellHeight * 0.3),
                                new Windows.Foundation.Point(cellWidth, barHeight),
                                new Windows.Foundation.Point(cellWidth, 0),
                                new Windows.Foundation.Point(cellWidth * 0.5, cellHeight * 0.3)
                            },
                            Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7)))
                        };
                        barGroup.Children.Add(rightFace);
                    }
                    else
                    {
                        var leftFace = new Microsoft.UI.Xaml.Shapes.Polygon
                        {
                            Points = new PointCollection
                            {
                                new Windows.Foundation.Point(0, barHeight),
                                new Windows.Foundation.Point(-cellWidth * 0.5, barHeight + cellHeight * 0.3),
                                new Windows.Foundation.Point(-cellWidth * 0.5, cellHeight * 0.3),
                                new Windows.Foundation.Point(0, 0)
                            },
                            Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(color.R * 0.7), (byte)(color.G * 0.7), (byte)(color.B * 0.7)))
                        };
                        barGroup.Children.Add(leftFace);
                    }

                    // Top face (always visible)
                    var topFace = new Microsoft.UI.Xaml.Shapes.Polygon
                    {
                        Points = new PointCollection
                        {
                            new Windows.Foundation.Point(0, 0),
                            new Windows.Foundation.Point(cellWidth * 0.5, cellHeight * 0.3),
                            new Windows.Foundation.Point(cellWidth, 0),
                            new Windows.Foundation.Point(cellWidth * 0.5, -cellHeight * 0.3)
                        },
                        Fill = new SolidColorBrush(Color.FromArgb(255, (byte)Math.Min(255, color.R * 1.3), (byte)Math.Min(255, color.G * 1.3), (byte)Math.Min(255, color.B * 1.3)))
                    };
                    barGroup.Children.Add(topFace);

                    Canvas.SetLeft(barGroup, isoX - cellWidth / 2);
                    Canvas.SetTop(barGroup, isoY - barHeight);
                    _3dHeatmapCanvas.Children.Add(barGroup);
                }
            }

            // Add axis labels
            var dayLabels = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int d = 0; d < 7; d++)
            {
                var baseX = -14 * cellWidth * 0.5;
                var baseY = (d - 3) * cellHeight * 0.5;
                var rotX = baseX * cosRot - baseY * sinRot;
                var rotY = baseX * sinRot + baseY * cosRot;

                var lbl = new TextBlock
                {
                    Text = dayLabels[d],
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
                };
                Canvas.SetLeft(lbl, offsetX + rotX - 20);
                Canvas.SetTop(lbl, offsetY + rotY * 0.6 + canvasHeight / 2 - 80);
                _3dHeatmapCanvas.Children.Add(lbl);
            }

            // Hour labels
            for (int h = 0; h < 24; h += 4)
            {
                var baseX = (h - 12) * cellWidth * 0.5;
                var baseY = 5 * cellHeight * 0.5;
                var rotX = baseX * cosRot - baseY * sinRot;
                var rotY = baseX * sinRot + baseY * cosRot;

                var lbl = new TextBlock
                {
                    Text = $"{h}:00",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
                };
                Canvas.SetLeft(lbl, offsetX + rotX - 12);
                Canvas.SetTop(lbl, offsetY + rotY * 0.6 + canvasHeight / 2 - 60);
                _3dHeatmapCanvas.Children.Add(lbl);
            }
        }

        private void Adjust3DZoom(double delta)
        {
            _3dZoomLevel = Math.Clamp(_3dZoomLevel + delta, 0.5, 3.0);
            if (_3dScaleTransform != null)
            {
                _3dScaleTransform.ScaleX = _3dZoomLevel;
                _3dScaleTransform.ScaleY = _3dZoomLevel;
            }
            if (_3dZoomLabel != null)
            {
                _3dZoomLabel.Text = $"{(int)(_3dZoomLevel * 100)}%";
            }
        }

        private void Adjust3DRotation(double degrees)
        {
            _3dRotationAngle = (_3dRotationAngle + degrees) % 360;
            if (_3dRotationAngle < 0) _3dRotationAngle += 360;

            // Redraw the canvas with new rotation
            if (_cached3DHourlyActivity != null && _3dHeatmapCanvas != null)
            {
                Draw3DHeatmapBars(_cached3DHourlyActivity, (int)_3dHeatmapCanvas.Width, (int)_3dHeatmapCanvas.Height);
            }
        }

        private void Reset3DView()
        {
            _3dZoomLevel = 1.0;
            _3dPanX = 0;
            _3dPanY = 0;
            _3dRotationAngle = 0;

            if (_3dScaleTransform != null)
            {
                _3dScaleTransform.ScaleX = 1;
                _3dScaleTransform.ScaleY = 1;
            }
            if (_3dTranslateTransform != null)
            {
                _3dTranslateTransform.X = 0;
                _3dTranslateTransform.Y = 0;
            }
            if (_3dZoomLabel != null)
            {
                _3dZoomLabel.Text = "100%";
            }

            // Redraw with reset rotation
            if (_cached3DHourlyActivity != null && _3dHeatmapCanvas != null)
            {
                Draw3DHeatmapBars(_cached3DHourlyActivity, (int)_3dHeatmapCanvas.Width, (int)_3dHeatmapCanvas.Height);
            }
        }

        private void On3DPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_3dHeatmapContainer == null) return;
            _3dIsDragging = true;
            _3dLastPointerPosition = e.GetCurrentPoint(_3dHeatmapContainer).Position;
            _3dHeatmapContainer.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void On3DPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_3dIsDragging || _3dHeatmapContainer == null || _3dTranslateTransform == null) return;

            var currentPos = e.GetCurrentPoint(_3dHeatmapContainer).Position;
            var deltaX = currentPos.X - _3dLastPointerPosition.X;
            var deltaY = currentPos.Y - _3dLastPointerPosition.Y;

            _3dPanX += deltaX;
            _3dPanY += deltaY;

            // Clamp pan to reasonable bounds
            var maxPan = 200;
            _3dPanX = Math.Clamp(_3dPanX, -maxPan, maxPan);
            _3dPanY = Math.Clamp(_3dPanY, -maxPan, maxPan);

            _3dTranslateTransform.X = _3dPanX;
            _3dTranslateTransform.Y = _3dPanY;

            _3dLastPointerPosition = currentPos;
            e.Handled = true;
        }

        private void On3DPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _3dIsDragging = false;
            if (_3dHeatmapContainer != null)
            {
                _3dHeatmapContainer.ReleasePointerCapture(e.Pointer);
            }
            e.Handled = true;
        }

        private void On3DPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _3dIsDragging = false;
        }

        private void On3DPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(_3dHeatmapContainer);
            var delta = point.Properties.MouseWheelDelta;

            // Zoom in/out based on scroll direction
            var zoomDelta = delta > 0 ? 0.15 : -0.15;
            Adjust3DZoom(zoomDelta);
            e.Handled = true;
        }

        private Color Get3DHeatColor(double intensity)
        {
            // Gradient from cool blue to hot pink/red
            if (intensity <= 0.2) return Color.FromArgb(255, 63, 81, 181);   // Indigo
            if (intensity <= 0.4) return Color.FromArgb(255, 3, 169, 244);   // Light Blue
            if (intensity <= 0.6) return Color.FromArgb(255, 76, 175, 80);   // Green
            if (intensity <= 0.8) return Color.FromArgb(255, 255, 152, 0);   // Orange
            return Color.FromArgb(255, 233, 30, 99);                          // Pink
        }

        private Border CreateDepthHeatmap(string title, List<DailyStats> dailyStats)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 16 };

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            titleStack.Children.Add(new FontIcon
            {
                Glyph = "\uE81E",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 103, 58, 183))
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(titleStack);

            stack.Children.Add(new TextBlock
            {
                Text = "Layered depth visualization showing weekly patterns",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                Margin = new Thickness(0, -8, 0, 0)
            });

            // Group by week
            var weekGroups = dailyStats
                .GroupBy(d => System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(d.Date, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                .OrderByDescending(g => g.Key)
                .Take(6)
                .ToList();

            var maxOps = dailyStats.Count > 0 ? dailyStats.Max(d => d.ExportOperations) : 1;
            if (maxOps == 0) maxOps = 1;

            // Create stacked "layers" with depth effect
            var layerContainer = new Grid { Height = 220 };

            int layerIndex = 0;
            foreach (var week in weekGroups)
            {
                var layerOpacity = 1.0 - layerIndex * 0.12;
                var layerScale = 1.0 - layerIndex * 0.06;
                var verticalOffset = layerIndex * 28;

                var layerBorder = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(layerIndex * 8, verticalOffset, layerIndex * 8, 0),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(40 + layerIndex * 15), 103, 58, 183)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(100 - layerIndex * 10), 103, 58, 183)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 8, 12, 8),
                    Opacity = layerOpacity
                };

                var layerContent = new Grid();
                for (int d = 0; d < 7; d++)
                    layerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var weekDays = week.OrderBy(d => d.Date).ToList();
                var dayNames = new[] { "M", "T", "W", "T", "F", "S", "S" };

                for (int d = 0; d < 7; d++)
                {
                    var dayData = weekDays.FirstOrDefault(dd => (int)dd.Date.DayOfWeek == (d + 1) % 7);
                    var ops = dayData?.ExportOperations ?? 0;
                    var intensity = (double)ops / maxOps;

                    var dayStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };

                    // Mini bar
                    var barContainer = new Border
                    {
                        Height = 35,
                        Width = 20,
                        Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                        CornerRadius = new CornerRadius(3)
                    };

                    var bar = new Border
                    {
                        Height = 5 + intensity * 30,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = new SolidColorBrush(GetDepthBarColor(intensity)),
                        CornerRadius = new CornerRadius(2)
                    };

                    var barGrid = new Grid { Height = 35 };
                    barGrid.Children.Add(barContainer);
                    barGrid.Children.Add(bar);
                    dayStack.Children.Add(barGrid);

                    dayStack.Children.Add(new TextBlock
                    {
                        Text = dayNames[d],
                        FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    Grid.SetColumn(dayStack, d);
                    layerContent.Children.Add(dayStack);
                }

                // Week label
                var weekLabel = new TextBlock
                {
                    Text = $"Week {week.Key}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var fullLayer = new StackPanel { Orientation = Orientation.Horizontal };
                fullLayer.Children.Add(weekLabel);
                fullLayer.Children.Add(layerContent);

                layerBorder.Child = fullLayer;
                layerContainer.Children.Add(layerBorder);
                layerIndex++;
            }

            stack.Children.Add(layerContainer);

            border.Child = stack;
            return border;
        }

        private Color GetDepthBarColor(double intensity)
        {
            // Gradient from light to bright
            if (intensity <= 0.25) return Color.FromArgb(255, 149, 117, 205); // Light purple
            if (intensity <= 0.5) return Color.FromArgb(255, 126, 87, 194);  // Medium purple
            if (intensity <= 0.75) return Color.FromArgb(255, 103, 58, 183); // Purple
            return Color.FromArgb(255, 81, 45, 168);                          // Deep purple
        }

        private Border CreateLineChart(string title, List<DailyStats> data)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (data.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                // Simple line chart using points
                var chartData = data.TakeLast(14).ToList();
                var maxOps = Math.Max(chartData.Max(d => d.ExportOperations), 1);
                var chartHeight = 100.0;
                var chartWidth = 300.0;

                var canvas = new Canvas { Width = chartWidth, Height = chartHeight + 20 };

                // Draw gridlines
                for (int i = 0; i <= 4; i++)
                {
                    var y = chartHeight - (i / 4.0 * chartHeight);
                    var line = new Line
                    {
                        X1 = 0, Y1 = y, X2 = chartWidth, Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                        StrokeThickness = 1
                    };
                    canvas.Children.Add(line);
                }

                // Draw data points and lines
                var pointWidth = chartWidth / (chartData.Count - 1);
                for (int i = 0; i < chartData.Count; i++)
                {
                    var day = chartData[i];
                    var x = i * pointWidth;
                    var y = chartHeight - ((double)day.ExportOperations / maxOps * chartHeight);

                    // Point
                    var point = new Ellipse
                    {
                        Width = 8, Height = 8,
                        Fill = new SolidColorBrush(AccentBlue)
                    };
                    Canvas.SetLeft(point, x - 4);
                    Canvas.SetTop(point, y - 4);
                    canvas.Children.Add(point);

                    // Line to next point
                    if (i < chartData.Count - 1)
                    {
                        var nextDay = chartData[i + 1];
                        var nextX = (i + 1) * pointWidth;
                        var nextY = chartHeight - ((double)nextDay.ExportOperations / maxOps * chartHeight);

                        var line = new Line
                        {
                            X1 = x, Y1 = y, X2 = nextX, Y2 = nextY,
                            Stroke = new SolidColorBrush(AccentBlue),
                            StrokeThickness = 2
                        };
                        canvas.Children.Add(line);
                    }
                }

                stack.Children.Add(canvas);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateValueLineChart(string title, List<DailyStats> data)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (data.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var chartData = data.TakeLast(14).ToList();
                var maxValue = Math.Max(chartData.Max(d => d.ExportPrice), 1);
                var chartHeight = 100.0;
                var chartWidth = 300.0;

                var canvas = new Canvas { Width = chartWidth, Height = chartHeight + 20 };

                // Draw data points and lines
                var pointWidth = chartWidth / (chartData.Count - 1);
                for (int i = 0; i < chartData.Count; i++)
                {
                    var day = chartData[i];
                    var x = i * pointWidth;
                    var y = chartHeight - ((double)day.ExportPrice / (double)maxValue * chartHeight);

                    // Point
                    var point = new Ellipse
                    {
                        Width = 8, Height = 8,
                        Fill = new SolidColorBrush(AccentOrange)
                    };
                    Canvas.SetLeft(point, x - 4);
                    Canvas.SetTop(point, y - 4);
                    canvas.Children.Add(point);

                    // Line to next point
                    if (i < chartData.Count - 1)
                    {
                        var nextDay = chartData[i + 1];
                        var nextX = (i + 1) * pointWidth;
                        var nextY = chartHeight - ((double)nextDay.ExportPrice / (double)maxValue * chartHeight);

                        var line = new Line
                        {
                            X1 = x, Y1 = y, X2 = nextX, Y2 = nextY,
                            Stroke = new SolidColorBrush(AccentOrange),
                            StrokeThickness = 2
                        };
                        canvas.Children.Add(line);
                    }
                }

                stack.Children.Add(canvas);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateHorizontalBarChart(string title, List<DailyStats> data)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };

            // Header
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (data.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxOps = Math.Max(data.Max(d => Math.Max(d.ImportOperations, d.ExportOperations)), 1);

                foreach (var day in data.TakeLast(7))
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

                    // Date label
                    var dateLabel = new TextBlock
                    {
                        Text = day.FormattedDate,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(dateLabel, 0);

                    // Bars
                    var barsContainer = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 8, 0) };

                    var importWidth = (day.ImportOperations / (double)maxOps) * BarMaxWidth;
                    var exportWidth = (day.ExportOperations / (double)maxOps) * BarMaxWidth;

                    barsContainer.Children.Add(new Rectangle
                    {
                        Width = Math.Max(importWidth, 4),
                        Height = 8,
                        Fill = new SolidColorBrush(AccentGreen),
                        RadiusX = 2,
                        RadiusY = 2,
                        HorizontalAlignment = HorizontalAlignment.Left
                    });

                    barsContainer.Children.Add(new Rectangle
                    {
                        Width = Math.Max(exportWidth, 4),
                        Height = 8,
                        Fill = new SolidColorBrush(AccentBlue),
                        RadiusX = 2,
                        RadiusY = 2,
                        HorizontalAlignment = HorizontalAlignment.Left
                    });

                    Grid.SetColumn(barsContainer, 1);

                    // Count
                    var countLabel = new TextBlock
                    {
                        Text = $"{day.ImportOperations}/{day.ExportOperations}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(countLabel, 2);

                    row.Children.Add(dateLabel);
                    row.Children.Add(barsContainer);
                    row.Children.Add(countLabel);

                    stack.Children.Add(row);
                }

                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 8, 0, 0) };
                legend.Children.Add(CreateLegendItem("Import", AccentGreen));
                legend.Children.Add(CreateLegendItem("Export", AccentBlue));
                stack.Children.Add(legend);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateVerticalColumnChart(string title, List<DailyStats> data)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };

            // Header
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (data.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var chartData = data.TakeLast(7).ToList();
                var maxPrice = Math.Max(chartData.Max(d => Math.Max(d.ImportPrice, d.ExportPrice)), 1);

                var chartContainer = new Grid { Height = ColumnMaxHeight + 30 };
                var columnsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                foreach (var day in chartData)
                {
                    var dayStack = new StackPanel { Width = 40 };

                    // Columns container
                    var colContainer = new Grid { Height = ColumnMaxHeight };
                    var colRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 2,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };

                    var importHeight = ((double)day.ImportPrice / (double)maxPrice) * ColumnMaxHeight;
                    var exportHeight = ((double)day.ExportPrice / (double)maxPrice) * ColumnMaxHeight;

                    colRow.Children.Add(new Rectangle
                    {
                        Width = 14,
                        Height = Math.Max(importHeight, 2),
                        Fill = new SolidColorBrush(AccentGreen),
                        RadiusX = 2,
                        RadiusY = 2
                    });

                    colRow.Children.Add(new Rectangle
                    {
                        Width = 14,
                        Height = Math.Max(exportHeight, 2),
                        Fill = new SolidColorBrush(AccentBlue),
                        RadiusX = 2,
                        RadiusY = 2
                    });

                    colContainer.Children.Add(colRow);
                    dayStack.Children.Add(colContainer);

                    // Date label
                    dayStack.Children.Add(new TextBlock
                    {
                        Text = day.Date.ToString("M/d"),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    });

                    columnsPanel.Children.Add(dayStack);
                }

                chartContainer.Children.Add(columnsPanel);
                stack.Children.Add(chartContainer);

                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                legend.Children.Add(CreateLegendItem("Import $", AccentGreen));
                legend.Children.Add(CreateLegendItem("Export $", AccentBlue));
                stack.Children.Add(legend);
            }

            border.Child = stack;
            return border;
        }

        private Border CreateLaborChart(List<DailyStats> data)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(new TextBlock
            {
                Text = "Labor Hours by Day",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (data.Count == 0)
            {
                stack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var chartData = data.TakeLast(7).ToList();
                var maxLabor = Math.Max(chartData.Max(d => Math.Max(d.ImportLabor, d.ExportLabor)), 1);

                foreach (var day in chartData)
                {
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

                    var dateLabel = new TextBlock
                    {
                        Text = day.Date.ToString("ddd"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(dateLabel, 0);

                    // Stacked bar
                    var barBg = new Border
                    {
                        Background = new SolidColorBrush(SectionBg),
                        CornerRadius = new CornerRadius(4),
                        Height = 16,
                        Margin = new Thickness(8, 0, 8, 0)
                    };

                    var totalLabor = day.ImportLabor + day.ExportLabor;
                    var barWidth = ((double)totalLabor / (double)(maxLabor * 2)) * 100;

                    var barStack = new StackPanel { Orientation = Orientation.Horizontal };
                    var importWidth = totalLabor > 0 ? ((double)day.ImportLabor / (double)totalLabor) * barWidth : 0;
                    var exportWidth = totalLabor > 0 ? ((double)day.ExportLabor / (double)totalLabor) * barWidth : 0;

                    barStack.Children.Add(new Rectangle
                    {
                        Width = Math.Max(importWidth * 2, 0),
                        Height = 16,
                        Fill = new SolidColorBrush(AccentOrange)
                    });
                    barStack.Children.Add(new Rectangle
                    {
                        Width = Math.Max(exportWidth * 2, 0),
                        Height = 16,
                        Fill = new SolidColorBrush(AccentPurple)
                    });

                    barBg.Child = barStack;
                    Grid.SetColumn(barBg, 1);

                    var hoursLabel = new TextBlock
                    {
                        Text = $"{day.ImportLabor:F1}/{day.ExportLabor:F1}h",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(hoursLabel, 2);

                    row.Children.Add(dateLabel);
                    row.Children.Add(barBg);
                    row.Children.Add(hoursLabel);

                    stack.Children.Add(row);
                }

                // Legend
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 8, 0, 0) };
                legend.Children.Add(CreateLegendItem("Import Labor", AccentOrange));
                legend.Children.Add(CreateLegendItem("Export Labor", AccentPurple));
                stack.Children.Add(legend);
            }

            border.Child = stack;
            return border;
        }

        #endregion

        #region Operations Tab

        private StackPanel BuildOperationsTab()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();
            var mostUsed = _exportStats.GetMostUsedOperationsByUser(_currentUserId, period);
            var opTypeBreakdown = _exportStats.GetOperationTypeBreakdown(_currentUserId, period);
            var categoryBreakdown = _exportStats.GetCategoryBreakdown(_currentUserId, period);

            // === SUMMARY CARDS ROW ===
            var summaryRow = new Grid();
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var totalOps = mostUsed.Sum(o => o.Count);
            var totalPrice = mostUsed.Sum(o => o.TotalPrice);
            var totalLabor = mostUsed.Sum(o => o.TotalLabor);
            var totalPaint = mostUsed.Sum(o => o.TotalPaint);
            var avgOpsPerExport = totalOps > 0 ? totalPrice / totalOps : 0;

            var opsCard = CreateStatCard("TOTAL OPERATIONS", totalOps.ToString("N0"), "Operations exported", AccentBlue, "\uE8B8", totalOps, "N0");
            var valueCard = CreateStatCard("TOTAL VALUE", totalPrice.ToString("C0"), "Dollar amount", AccentGreen, "\uE8D4", (double)totalPrice, "C0");
            var laborCard = CreateStatCard("LABOR HOURS", totalLabor.ToString("F1"), "Hours billed", AccentPurple, "\uE823", (double)totalLabor, "F1");
            var avgCard = CreateStatCard("AVG $/OPERATION", avgOpsPerExport.ToString("C2"), "Per operation", AccentOrange, "\uE9D9", (double)avgOpsPerExport, "C2");

            Grid.SetColumn(opsCard, 0);
            Grid.SetColumn(valueCard, 2);
            Grid.SetColumn(laborCard, 4);
            Grid.SetColumn(avgCard, 6);
            summaryRow.Children.Add(opsCard);
            summaryRow.Children.Add(valueCard);
            summaryRow.Children.Add(laborCard);
            summaryRow.Children.Add(avgCard);
            stack.Children.Add(summaryRow);

            // === TWO COLUMN LAYOUT FOR BREAKDOWNS ===
            var twoColGrid = new Grid();
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // === LEFT COLUMN: Operation Type Breakdown ===
            var opTypeSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var opTypeStack = new StackPanel { Spacing = 12 };
            var opTypeHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            opTypeHeader.Children.Add(new FontIcon { Glyph = "\uE8CB", FontSize = 14, Foreground = new SolidColorBrush(AccentPurple) });
            opTypeHeader.Children.Add(new TextBlock
            {
                Text = "Operation Type Breakdown",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            opTypeStack.Children.Add(opTypeHeader);

            if (opTypeBreakdown.Count == 0)
            {
                opTypeStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxOpType = opTypeBreakdown.Max(o => o.Count);
                var opTypeColors = new[] { AccentBlue, AccentGreen, AccentPurple, AccentOrange, AccentRed, AccentCyan };
                int colorIdx = 0;

                foreach (var opType in opTypeBreakdown.Take(8))
                {
                    var percent = totalOps > 0 ? (opType.Count * 100.0 / totalOps) : 0;
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });

                    var typeLabel = new TextBlock
                    {
                        Text = opType.OperationType,
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(typeLabel, 0);

                    var barWidth = (opType.Count / (double)maxOpType) * 100;
                    var bar = new Rectangle
                    {
                        Width = Math.Max(barWidth, 4),
                        Height = 14,
                        Fill = new SolidColorBrush(opTypeColors[colorIdx++ % opTypeColors.Length]),
                        RadiusX = 3,
                        RadiusY = 3,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    Grid.SetColumn(bar, 1);

                    var countLabel = new TextBlock
                    {
                        Text = opType.Count.ToString(),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(AccentBlue),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(countLabel, 2);

                    var percentLabel = new TextBlock
                    {
                        Text = $"{percent:F1}%",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(percentLabel, 3);

                    var priceLabel = new TextBlock
                    {
                        Text = opType.TotalPrice.ToString("C0"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(AccentGreen),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(priceLabel, 4);

                    row.Children.Add(typeLabel);
                    row.Children.Add(bar);
                    row.Children.Add(countLabel);
                    row.Children.Add(percentLabel);
                    row.Children.Add(priceLabel);
                    opTypeStack.Children.Add(row);
                }
            }

            opTypeSection.Child = opTypeStack;
            Grid.SetColumn(opTypeSection, 0);
            twoColGrid.Children.Add(opTypeSection);

            // === RIGHT COLUMN: Category Breakdown ===
            var catSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var catStack = new StackPanel { Spacing = 12 };
            var catHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            catHeader.Children.Add(new FontIcon { Glyph = "\uE9D9", FontSize = 14, Foreground = new SolidColorBrush(AccentOrange) });
            catHeader.Children.Add(new TextBlock
            {
                Text = "Value by Category",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            catStack.Children.Add(catHeader);

            var catTotal = categoryBreakdown.LaborDollars + categoryBreakdown.RefinishDollars +
                          categoryBreakdown.MaterialsDollars + categoryBreakdown.OtherDollars;

            if (catTotal == 0)
            {
                catStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                // Labor
                catStack.Children.Add(BuildCategoryRow("Labor", categoryBreakdown.LaborDollars, catTotal, AccentBlue));
                // Refinish
                catStack.Children.Add(BuildCategoryRow("Refinish", categoryBreakdown.RefinishDollars, catTotal, AccentPurple));
                // Materials
                catStack.Children.Add(BuildCategoryRow("Materials", categoryBreakdown.MaterialsDollars, catTotal, AccentOrange));
                // Other
                if (categoryBreakdown.OtherDollars > 0)
                    catStack.Children.Add(BuildCategoryRow("Other", categoryBreakdown.OtherDollars, catTotal, AccentCyan));

                // Pie chart visualization
                catStack.Children.Add(BuildCategoryPieChart(categoryBreakdown, catTotal));
            }

            catSection.Child = catStack;
            Grid.SetColumn(catSection, 2);
            twoColGrid.Children.Add(catSection);

            stack.Children.Add(twoColGrid);

            // === MOST USED OPERATIONS (DETAILED) ===
            var mostUsedSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var sectionStack = new StackPanel { Spacing = 12 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE8B8", FontSize = 14, Foreground = new SolidColorBrush(AccentBlue) });
            header.Children.Add(new TextBlock
            {
                Text = "Top 15 Operations by Usage",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            sectionStack.Children.Add(header);

            // Column headers
            var colHeaders = new Grid { Margin = new Thickness(0, 8, 0, 4) };
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            var headerLabels = new[] { "#", "Type", "", "Count", "Value", "Labor", "Paint" };
            for (int i = 0; i < headerLabels.Length; i++)
            {
                var lbl = new TextBlock
                {
                    Text = headerLabels[i],
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                    HorizontalAlignment = i >= 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                Grid.SetColumn(lbl, i);
                colHeaders.Children.Add(lbl);
            }
            sectionStack.Children.Add(colHeaders);

            if (mostUsed.Count == 0)
            {
                sectionStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxCount = mostUsed.Max(o => o.Count);

                // Show top 15
                for (int i = 0; i < Math.Min(mostUsed.Count, 15); i++)
                {
                    var op = mostUsed[i];
                    var opRow = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

                    // Rank badge
                    var rankColor = i < 3 ? (i == 0 ? Color.FromArgb(255, 255, 215, 0) : (i == 1 ? Color.FromArgb(255, 192, 192, 192) : Color.FromArgb(255, 205, 127, 50))) : SectionBg;
                    var rankBadge = new Border
                    {
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(11),
                        Background = new SolidColorBrush(rankColor)
                    };
                    rankBadge.Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(i < 3 ? Colors.Black : Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(rankBadge, 0);

                    // Operation type
                    var opLabel = new TextBlock
                    {
                        Text = op.OperationType,
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(opLabel, 1);

                    // Bar
                    var barWidth = (op.Count / (double)maxCount) * 120;
                    var bar = new Rectangle
                    {
                        Width = Math.Max(barWidth, 4),
                        Height = 14,
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0, 0),
                            EndPoint = new Windows.Foundation.Point(1, 0),
                            GradientStops = {
                                new GradientStop { Color = AccentBlue, Offset = 0 },
                                new GradientStop { Color = AccentPurple, Offset = 1 }
                            }
                        },
                        RadiusX = 3,
                        RadiusY = 3,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    Grid.SetColumn(bar, 2);

                    // Count
                    var countLabel = new TextBlock
                    {
                        Text = op.Count.ToString(),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(AccentBlue),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(countLabel, 3);

                    // Value
                    var valueLabel = new TextBlock
                    {
                        Text = op.TotalPrice.ToString("C0"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(AccentGreen),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(valueLabel, 4);

                    // Labor hours
                    var laborLabel = new TextBlock
                    {
                        Text = op.TotalLabor.ToString("F1"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(laborLabel, 5);

                    // Paint hours
                    var paintLabel = new TextBlock
                    {
                        Text = op.TotalPaint.ToString("F1"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(paintLabel, 6);

                    opRow.Children.Add(rankBadge);
                    opRow.Children.Add(opLabel);
                    opRow.Children.Add(bar);
                    opRow.Children.Add(countLabel);
                    opRow.Children.Add(valueLabel);
                    opRow.Children.Add(laborLabel);
                    opRow.Children.Add(paintLabel);

                    sectionStack.Children.Add(opRow);
                }
            }

            mostUsedSection.Child = sectionStack;
            stack.Children.Add(mostUsedSection);

            // === EFFICIENCY METRICS ===
            var efficiencySection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var effStack = new StackPanel { Spacing = 12 };
            var effHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            effHeader.Children.Add(new FontIcon { Glyph = "\uE945", FontSize = 14, Foreground = new SolidColorBrush(AccentCyan) });
            effHeader.Children.Add(new TextBlock
            {
                Text = "Efficiency Metrics",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            effStack.Children.Add(effHeader);

            var effGrid = new Grid();
            effGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            effGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            effGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            effGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avgValuePerHour = totalLabor > 0 ? totalPrice / (decimal)totalLabor : 0;
            var avgLaborPerOp = totalOps > 0 ? totalLabor / totalOps : 0;
            var avgPaintPerOp = totalOps > 0 ? totalPaint / totalOps : 0;

            effGrid.Children.Add(CreateEfficiencyMetric("$/Labor Hour", avgValuePerHour.ToString("C2"), AccentGreen, 0));
            effGrid.Children.Add(CreateEfficiencyMetric("Avg Labor/Op", avgLaborPerOp.ToString("F2") + "h", AccentBlue, 1));
            effGrid.Children.Add(CreateEfficiencyMetric("Avg Paint/Op", avgPaintPerOp.ToString("F2") + "h", AccentPurple, 2));
            effGrid.Children.Add(CreateEfficiencyMetric("Unique Op Types", mostUsed.Count.ToString(), AccentOrange, 3));

            effStack.Children.Add(effGrid);
            efficiencySection.Child = effStack;
            stack.Children.Add(efficiencySection);

            return stack;
        }

        private UIElement BuildCategoryRow(string name, decimal value, decimal total, Color color)
        {
            var percent = total > 0 ? (value / total * 100) : 0;
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });

            var nameLabel = new TextBlock
            {
                Text = name,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameLabel, 0);

            var barWidth = (double)(percent) * 1.2;
            var bar = new Rectangle
            {
                Width = Math.Max(barWidth, 4),
                Height = 16,
                Fill = new SolidColorBrush(color),
                RadiusX = 4,
                RadiusY = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(bar, 1);

            var valueLabel = new TextBlock
            {
                Text = value.ToString("C0"),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueLabel, 2);

            var percentLabel = new TextBlock
            {
                Text = $"{percent:F1}%",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(percentLabel, 3);

            row.Children.Add(nameLabel);
            row.Children.Add(bar);
            row.Children.Add(valueLabel);
            row.Children.Add(percentLabel);

            return row;
        }

        private UIElement BuildCategoryPieChart(CategoryBreakdown breakdown, decimal total)
        {
            var canvas = new Canvas
            {
                Width = 120,
                Height = 120,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            double startAngle = 0;
            var categories = new[]
            {
                (breakdown.LaborDollars, AccentBlue, "Labor"),
                (breakdown.RefinishDollars, AccentPurple, "Refinish"),
                (breakdown.MaterialsDollars, AccentOrange, "Materials"),
                (breakdown.OtherDollars, AccentCyan, "Other")
            };

            foreach (var (value, color, name) in categories)
            {
                if (value <= 0) continue;

                var percent = (double)(value / total);
                var sweepAngle = percent * 360;

                var path = CreatePieSlice(60, 60, 50, startAngle, sweepAngle, color);
                canvas.Children.Add(path);

                startAngle += sweepAngle;
            }

            // Center hole (donut style)
            var centerCircle = new Ellipse
            {
                Width = 50,
                Height = 50,
                Fill = new SolidColorBrush(CardBg)
            };
            Canvas.SetLeft(centerCircle, 35);
            Canvas.SetTop(centerCircle, 35);
            canvas.Children.Add(centerCircle);

            // Center text
            var centerText = new TextBlock
            {
                Text = total.ToString("C0"),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Canvas.SetLeft(centerText, 42);
            Canvas.SetTop(centerText, 52);
            canvas.Children.Add(centerText);

            return canvas;
        }

        private Microsoft.UI.Xaml.Shapes.Path CreatePieSlice(double centerX, double centerY, double radius, double startAngle, double sweepAngle, Color color)
        {
            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var x1 = centerX + radius * Math.Cos(startRad);
            var y1 = centerY + radius * Math.Sin(startRad);
            var x2 = centerX + radius * Math.Cos(endRad);
            var y2 = centerY + radius * Math.Sin(endRad);

            var largeArc = sweepAngle > 180;

            var pathFigure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(centerX, centerY),
                IsClosed = true
            };

            pathFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x1, y1) });
            pathFigure.Segments.Add(new ArcSegment
            {
                Point = new Windows.Foundation.Point(x2, y2),
                Size = new Windows.Foundation.Size(radius, radius),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Clockwise
            });
            pathFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(centerX, centerY) });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = new SolidColorBrush(color),
                Data = pathGeometry
            };
        }

        private UIElement CreateEfficiencyMetric(string label, string value, Color color, int column)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(8)
            };

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            Grid.SetColumn(stack, column);
            return stack;
        }

        private StackPanel CreateSummaryStat(string label, string value, int column)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetColumn(stack, column);
            return stack;
        }

        #endregion

        #region PDFs Tab

        private StackPanel BuildPdfsTab()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();
            var pdfStats = _exportStats.GetPdfStatsByUser(_currentUserId, period);
            var recentPdfs = _exportStats.GetRecentPdfExportsByUser(_currentUserId, 15);

            // Summary cards
            var summaryRow = new Grid();
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var totalCard = CreateStatCard("PDFs EXPORTED", pdfStats.TotalExports.ToString(),
                "Total exports", AccentRed, "\uE8A5", pdfStats.TotalExports, "N0");
            var pagesCard = CreateStatCard("TOTAL PAGES", pdfStats.TotalPages.ToString(),
                "Pages generated", AccentPurple, "\uE8C4", pdfStats.TotalPages, "N0");
            var sizeCard = CreateStatCard("TOTAL SIZE", pdfStats.FormattedSize,
                "Data exported", AccentCyan, "\uE8B7");

            Grid.SetColumn(totalCard, 0);
            Grid.SetColumn(pagesCard, 2);
            Grid.SetColumn(sizeCard, 4);
            summaryRow.Children.Add(totalCard);
            summaryRow.Children.Add(pagesCard);
            summaryRow.Children.Add(sizeCard);
            stack.Children.Add(summaryRow);

            // By type breakdown
            if (pdfStats.ByType.Count > 0)
            {
                var typeSection = new Border
                {
                    Background = new SolidColorBrush(CardBg),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16)
                };

                var typeStack = new StackPanel { Spacing = 8 };
                typeStack.Children.Add(new TextBlock
                {
                    Text = "By Export Type",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });

                var maxType = pdfStats.ByType.Max(t => t.Value);
                var colors = new[] { AccentBlue, AccentGreen, AccentPurple, AccentOrange, AccentCyan };
                int colorIndex = 0;

                foreach (var (type, count) in pdfStats.ByType.OrderByDescending(t => t.Value))
                {
                    var typeRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                    var typeLabel = new TextBlock
                    {
                        Text = type,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(typeLabel, 0);

                    var barWidth = (count / (double)maxType) * 200;
                    var bar = new Rectangle
                    {
                        Width = Math.Max(barWidth, 4),
                        Height = 14,
                        Fill = new SolidColorBrush(colors[colorIndex++ % colors.Length]),
                        RadiusX = 3,
                        RadiusY = 3,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(8, 0, 8, 0)
                    };
                    Grid.SetColumn(bar, 1);

                    var countLabel = new TextBlock
                    {
                        Text = count.ToString(),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(AccentBlue),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(countLabel, 2);

                    typeRow.Children.Add(typeLabel);
                    typeRow.Children.Add(bar);
                    typeRow.Children.Add(countLabel);

                    typeStack.Children.Add(typeRow);
                }

                typeSection.Child = typeStack;
                stack.Children.Add(typeSection);
            }

            // Recent exports
            var recentSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var recentStack = new StackPanel { Spacing = 8 };
            recentStack.Children.Add(new TextBlock
            {
                Text = "Recent PDF Exports",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (recentPdfs.Count == 0)
            {
                recentStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                foreach (var pdf in recentPdfs.Take(10))
                {
                    var pdfRow = CreatePdfActivityItem(pdf);
                    recentStack.Children.Add(pdfRow);
                }
            }

            recentSection.Child = recentStack;
            stack.Children.Add(recentSection);

            return stack;
        }

        private Border CreatePdfActivityItem(PdfExportRecord pdf)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon
            var icon = new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentRed),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(icon, 0);

            // Info
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = pdf.FileName.Length > 30 ? pdf.FileName.Substring(0, 27) + "..." : pdf.FileName,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White)
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{pdf.ExportType} • {pdf.PageCount} pages",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });
            Grid.SetColumn(infoStack, 1);

            // Time
            var timeLabel = new TextBlock
            {
                Text = pdf.Timestamp.ToString("h:mm tt"),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeLabel, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(timeLabel);

            border.Child = grid;
            return border;
        }

        #endregion

        #region Activity Tab

        private StackPanel BuildActivityTab()
        {
            var stack = new StackPanel { Spacing = 16 };
            var transactions = _exportStats.GetRecentTransactionsByUser(_currentUserId, 25);

            // Activity timeline section
            var timelineSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var timelineStack = new StackPanel { Spacing = 8 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE823",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentBlue)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Recent Activity",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            timelineStack.Children.Add(header);

            if (transactions.Count == 0)
            {
                timelineStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                // Group by date
                var groupedByDate = transactions.GroupBy(t => t.Timestamp.Date).ToList();

                foreach (var dateGroup in groupedByDate.Take(5))
                {
                    // Date header
                    var dateHeader = new TextBlock
                    {
                        Text = dateGroup.Key == DateTime.Today ? "Today" :
                               dateGroup.Key == DateTime.Today.AddDays(-1) ? "Yesterday" :
                               dateGroup.Key.ToString("dddd, MMM d"),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    timelineStack.Children.Add(dateHeader);

                    foreach (var tx in dateGroup.Take(8))
                    {
                        var item = CreateActivityItem(tx);
                        timelineStack.Children.Add(item);
                    }
                }
            }

            timelineSection.Child = timelineStack;
            stack.Children.Add(timelineSection);

            // Hourly breakdown for today
            var todayTransactions = transactions.Where(t => t.Timestamp.Date == DateTime.Today).ToList();
            if (todayTransactions.Count > 0)
            {
                stack.Children.Add(BuildHourlyBreakdown(todayTransactions));
            }

            return stack;
        }

        private Border CreateActivityItem(TransactionRecord tx)
        {
            var isImport = tx.Type == TransactionType.Import;
            var accentColor = isImport ? AccentGreen : AccentBlue;

            var border = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel();

            var typeText = isImport
                ? $"↓ Import from {tx.Source}"
                : $"↑ Export to {tx.Target}";
            infoStack.Children.Add(new TextBlock
            {
                Text = typeText,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            infoStack.Children.Add(new TextBlock
            {
                Text = $"{tx.OperationCount} ops • {tx.TotalPrice:C0} • {tx.TotalLabor:F1}h labor",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
            });

            Grid.SetColumn(infoStack, 0);

            var timeLabel = new TextBlock
            {
                Text = tx.Timestamp.ToString("h:mm tt"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeLabel, 1);

            grid.Children.Add(infoStack);
            grid.Children.Add(timeLabel);

            border.Child = grid;
            return border;
        }

        private Border BuildHourlyBreakdown(List<TransactionRecord> todayTransactions)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { Spacing = 8 };

            stack.Children.Add(new TextBlock
            {
                Text = "Today's Activity by Hour",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var hourlyGroups = todayTransactions
                .GroupBy(t => t.Timestamp.Hour)
                .OrderBy(g => g.Key)
                .ToList();

            var maxCount = hourlyGroups.Max(g => g.Count());

            foreach (var hourGroup in hourlyGroups)
            {
                var hourRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                hourRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                hourRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                hourRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

                var hourLabel = new TextBlock
                {
                    Text = DateTime.Today.AddHours(hourGroup.Key).ToString("h tt"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(hourLabel, 0);

                var barWidth = (hourGroup.Count() / (double)maxCount) * 200;
                var importCount = hourGroup.Count(t => t.Type == TransactionType.Import);
                var exportCount = hourGroup.Count(t => t.Type == TransactionType.Export);

                var barBorder = new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Windows.Foundation.Point(0, 0),
                        EndPoint = new Windows.Foundation.Point(1, 0),
                        GradientStops = {
                            new GradientStop { Color = AccentGreen, Offset = 0 },
                            new GradientStop { Color = AccentBlue, Offset = 1 }
                        }
                    },
                    Width = Math.Max(barWidth, 10),
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(8, 0, 8, 0)
                };
                Grid.SetColumn(barBorder, 1);

                var countLabel = new TextBlock
                {
                    Text = $"{importCount}↓ {exportCount}↑",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(countLabel, 2);

                hourRow.Children.Add(hourLabel);
                hourRow.Children.Add(barBorder);
                hourRow.Children.Add(countLabel);

                stack.Children.Add(hourRow);
            }

            // Peak hour
            var peakHour = hourlyGroups.OrderByDescending(g => g.Count()).First();
            stack.Children.Add(new TextBlock
            {
                Text = $"Peak: {DateTime.Today.AddHours(peakHour.Key):h tt} ({peakHour.Count()} transactions)",
                FontSize = 10,
                Foreground = new SolidColorBrush(AccentBlue),
                Margin = new Thickness(0, 8, 0, 0)
            });

            border.Child = stack;
            return border;
        }

        #endregion

        #region Leaderboard Tab

        private StackPanel BuildLeaderboardTab()
        {
            var stack = new StackPanel { Spacing = 16 };
            var period = GetSelectedPeriod();
            var userRanking = _exportStats.GetUserRanking(period);
            var estimateRanking = _estimateStats.GetUserRanking(period);

            // Operations leaderboard
            var opsSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var opsStack = new StackPanel { Spacing = 12 };

            var header1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header1.Children.Add(new FontIcon
            {
                Glyph = "\uE8D4",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentOrange)
            });
            header1.Children.Add(new TextBlock
            {
                Text = "Operations Leaderboard",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            opsStack.Children.Add(header1);

            if (userRanking.Count == 0)
            {
                opsStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxOps = userRanking.Max(u => u.TotalOperations);

                for (int i = 0; i < userRanking.Count && i < 10; i++)
                {
                    var user = userRanking[i];
                    var isCurrentUser = user.UserId == _currentUserId;

                    opsStack.Children.Add(CreateLeaderboardRow(i + 1, user.UserId, user.TotalOperations.ToString(),
                        $"{user.FormattedPrice} • {user.FormattedLabor}",
                        user.TotalOperations, maxOps, isCurrentUser));
                }
            }

            opsSection.Child = opsStack;
            stack.Children.Add(opsSection);

            // Estimates leaderboard
            var estimatesSection = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var estStack = new StackPanel { Spacing = 12 };

            var header2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header2.Children.Add(new FontIcon
            {
                Glyph = "\uE8D4",
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentPurple)
            });
            header2.Children.Add(new TextBlock
            {
                Text = "Estimates Leaderboard",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            estStack.Children.Add(header2);

            if (estimateRanking.Count == 0)
            {
                estStack.Children.Add(CreateNoDataMessage());
            }
            else
            {
                var maxEst = estimateRanking.Max(u => u.TotalImports);

                for (int i = 0; i < estimateRanking.Count && i < 10; i++)
                {
                    var user = estimateRanking[i];
                    var isCurrentUser = user.UserId == _currentUserId;

                    estStack.Children.Add(CreateLeaderboardRow(i + 1, user.UserId, user.TotalImports.ToString(),
                        $"{user.TotalPatternsLearned} patterns • {user.TotalOperationsGenerated} ops",
                        user.TotalImports, maxEst, isCurrentUser));
                }
            }

            estimatesSection.Child = estStack;
            stack.Children.Add(estimatesSection);

            return stack;
        }

        private Grid CreateLeaderboardRow(int rank, string userName, string value, string subtext, int count, int max, bool isCurrentUser)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            // Rank badge
            var badgeColor = rank switch
            {
                1 => Color.FromArgb(255, 255, 215, 0),  // Gold
                2 => Color.FromArgb(255, 192, 192, 192),  // Silver
                3 => Color.FromArgb(255, 205, 127, 50),  // Bronze
                _ => SectionBg
            };

            var rankBadge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(badgeColor)
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

            // User name
            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock
            {
                Text = userName,
                FontSize = 11,
                FontWeight = isCurrentUser ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(isCurrentUser ? AccentBlue : Colors.White)
            };
            nameStack.Children.Add(nameText);

            if (isCurrentUser)
            {
                nameStack.Children.Add(new TextBlock
                {
                    Text = "(You)",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(AccentBlue)
                });
            }
            Grid.SetColumn(nameStack, 1);

            // Progress bar
            var barWidth = (count / (double)max) * 150;
            var barContainer = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            barContainer.Children.Add(new Rectangle
            {
                Width = Math.Max(barWidth, 4),
                Height = 16,
                Fill = new SolidColorBrush(isCurrentUser ? AccentBlue : Color.FromArgb(255, 70, 70, 70)),
                RadiusX = 4,
                RadiusY = 4,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            barContainer.Children.Add(new TextBlock
            {
                Text = subtext,
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            Grid.SetColumn(barContainer, 2);

            // Value
            var valueLabel = new TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(isCurrentUser ? AccentBlue : Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(valueLabel, 3);

            row.Children.Add(rankBadge);
            row.Children.Add(nameStack);
            row.Children.Add(barContainer);
            row.Children.Add(valueLabel);

            return row;
        }

        #endregion

        #region Helpers

        private StackPanel CreateLegendItem(string label, Color color)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            stack.Children.Add(new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(color),
                RadiusX = 2,
                RadiusY = 2
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                VerticalAlignment = VerticalAlignment.Center
            });
            return stack;
        }

        private TextBlock CreateNoDataMessage()
        {
            return new TextBlock
            {
                Text = "No data available for this period",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 16)
            };
        }

        private async void ClearStats_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear Statistics?",
                Content = $"This will permanently delete all statistics for {_currentUserId}. This cannot be undone.",
                PrimaryButtonText = "Clear All",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _exportStats.ClearAllStats();
                _estimateStats.ClearAllStats();
                RefreshStats();
            }
        }

        public void Refresh()
        {
            RefreshUserList();
            RefreshStats();
        }

        #endregion

        #region Chart Helpers

        private void DrawChartAxes(Canvas canvas, double plotL, double plotT, double plotR, double plotB, double maxVal, string yFormat, int gridlines = 4)
        {
            var plotH = plotB - plotT;
            for (int i = 0; i <= gridlines; i++)
            {
                var y = plotB - (plotH * i / gridlines);
                var val = maxVal * i / gridlines;
                canvas.Children.Add(new Line { X1 = plotL, Y1 = y, X2 = plotR, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), StrokeThickness = 1 });
                var label = new TextBlock { Text = val.ToString(yFormat), FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), TextAlignment = TextAlignment.Right, Width = 40 };
                Canvas.SetLeft(label, plotL - 45);
                Canvas.SetTop(label, y - 7);
                canvas.Children.Add(label);
            }
            canvas.Children.Add(new Line { X1 = plotL, Y1 = plotT, X2 = plotL, Y2 = plotB, Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = plotL, Y1 = plotB, X2 = plotR, Y2 = plotB, Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), StrokeThickness = 1 });
        }

        private void DrawChartDateLabels(Canvas canvas, List<DailyStats> data, double plotL, double plotB, double xStep, int maxLabels = 7)
        {
            if (data.Count <= 1) return;
            int interval = Math.Max(1, (int)Math.Ceiling((double)data.Count / maxLabels));
            for (int i = 0; i < data.Count; i += interval)
            {
                var x = plotL + i * xStep;
                var label = new TextBlock { Text = data[i].Date.ToString("MMM dd"), FontSize = 9, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)) };
                Canvas.SetLeft(label, x - 18);
                Canvas.SetTop(label, plotB + 6);
                canvas.Children.Add(label);
            }
        }

        private void AnimateColumnHeight(Border col, double targetHeight, int delayMs = 0, int durationMs = 500)
        {
            if (targetHeight <= 0) return;
            col.Height = 0;

            void StartAnimation()
            {
                var startTime = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (s, e) =>
                {
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var t = Math.Min(elapsed / durationMs, 1.0);
                    var progress = 1 - Math.Pow(1 - t, 3);
                    col.Height = targetHeight * progress;
                    if (t >= 1.0) { col.Height = targetHeight; timer.Stop(); }
                };
                timer.Start();
            }

            if (delayMs <= 0) StartAnimation();
            else
            {
                var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                delayTimer.Tick += (s, e) => { delayTimer.Stop(); StartAnimation(); };
                delayTimer.Start();
            }
        }

        #endregion

        #region Animations

        private void AnimateCountUp(TextBlock target, double endValue, string format, int durationMs = 800)
        {
            if (endValue == 0) return;
            target.Text = (0.0).ToString(format);
            var startTime = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var t = Math.Min(elapsed / durationMs, 1.0);
                var progress = 1 - Math.Pow(1 - t, 3);
                target.Text = (endValue * progress).ToString(format);
                if (t >= 1.0)
                {
                    target.Text = endValue.ToString(format);
                    timer.Stop();
                }
            };
            timer.Start();
        }

        private void AnimateBarWidth(Border bar, double targetWidth, int delayMs = 0, int durationMs = 500)
        {
            if (targetWidth <= 0) return;
            bar.Width = 0;

            void StartAnimation()
            {
                var startTime = DateTime.UtcNow;
                var animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                animTimer.Tick += (s, e) =>
                {
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var t = Math.Min(elapsed / durationMs, 1.0);
                    var progress = 1 - Math.Pow(1 - t, 3);
                    bar.Width = targetWidth * progress;
                    if (t >= 1.0)
                    {
                        bar.Width = targetWidth;
                        animTimer.Stop();
                    }
                };
                animTimer.Start();
            }

            if (delayMs <= 0)
            {
                StartAnimation();
            }
            else
            {
                var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                delayTimer.Tick += (s, e) =>
                {
                    delayTimer.Stop();
                    StartAnimation();
                };
                delayTimer.Start();
            }
        }

        private void AddSparkline(StackPanel parent, List<double> dataPoints, Color color, double width = 50, double height = 16)
        {
            if (dataPoints.Count < 2) return;
            var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 2, 0, 0) };
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5,
                Opacity = 0.7
            };

            var maxVal = dataPoints.Max();
            var minVal = dataPoints.Min();
            var range = maxVal - minVal;
            if (range == 0) range = 1;

            for (int i = 0; i < dataPoints.Count; i++)
            {
                var x = (double)i / (dataPoints.Count - 1) * width;
                var y = height - ((dataPoints[i] - minVal) / range) * (height - 2) - 1;
                polyline.Points.Add(new Windows.Foundation.Point(x, y));
            }

            canvas.Children.Add(polyline);
            parent.Children.Add(canvas);
        }

        #endregion
    }
}
