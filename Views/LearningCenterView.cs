#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McStudDesktop.Services;
using McstudDesktop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Learning Center - Upload estimates, view stats, query learned data
    /// </summary>
    public sealed class LearningCenterView : UserControl
    {
        private TextBox _queryInput = null!;
        private TextBlock _queryResult = null!;
        private TextBlock _statsText = null!;
        private ListView _patternsListView = null!;
        private ListView _parsedItemsListView = null!;
        private TextBlock _importStatusText = null!;
        private ProgressRing _importProgress = null!;

        public LearningCenterView()
        {
            BuildUI();
            RefreshStats();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 28)),
                Padding = new Thickness(20)
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var header = new TextBlock
            {
                Text = "Learning Center",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Main content with tabs
            var tabView = new TabView
            {
                TabWidthMode = TabViewWidthMode.SizeToContent,
                IsAddTabButtonVisible = false,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35))
            };

            // Tab 1: Query Operations
            var queryTab = new TabViewItem
            {
                Header = "Query Operations",
                Content = CreateQueryPanel()
            };
            tabView.TabItems.Add(queryTab);

            // Tab 2: Insurance Payments (shop-owner only — never client-facing)
            var paymentsTab = new TabViewItem
            {
                Header = "Insurance Payments",
                Content = CreateInsurancePaymentsPanel()
            };
            tabView.TabItems.Add(paymentsTab);

            // Tab 3: Estimates Browser — full list, filterable
            var browserTab = new TabViewItem
            {
                Header = "Estimates Browser",
                Content = CreateEstimatesBrowserPanel()
            };
            tabView.TabItems.Add(browserTab);

            // Tab 4: By Insurer — grouped view
            var byInsurerTab = new TabViewItem
            {
                Header = "By Insurer",
                Content = CreateByInsurerPanel()
            };
            tabView.TabItems.Add(byInsurerTab);

            // Tab: Import Estimates
            var importTab = new TabViewItem
            {
                Header = "Import Estimates",
                Content = CreateImportPanel()
            };
            tabView.TabItems.Add(importTab);

            // Tab 3: Learning Stats
            var statsTab = new TabViewItem
            {
                Header = "Statistics",
                Content = CreateStatsPanel()
            };
            tabView.TabItems.Add(statsTab);

            // Tab 4: Patterns Database
            var patternsTab = new TabViewItem
            {
                Header = "Patterns Database",
                Content = CreatePatternsPanel()
            };
            tabView.TabItems.Add(patternsTab);

            // Tab 5: Tips & Tricks
            var tipsTab = new TabViewItem
            {
                Header = "Tips & Tricks",
                Content = CreateTipsPanel()
            };
            tabView.TabItems.Add(tipsTab);

            Grid.SetRow(tabView, 1);
            mainGrid.Children.Add(tabView);

            Content = mainGrid;
        }

        private UIElement CreateQueryPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Instructions
            panel.Children.Add(new TextBlock
            {
                Text = "Ask questions about operations based on learned estimates",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                FontSize = 14
            });

            // Query input
            var inputStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            _queryInput = new TextBox
            {
                PlaceholderText = "Example: I'm repairing a door, what operations do I need?",
                Width = 500,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            inputStack.Children.Add(_queryInput);

            var queryButton = new Button
            {
                Content = "Search",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            queryButton.Click += QueryButton_Click;
            inputStack.Children.Add(queryButton);

            panel.Children.Add(inputStack);

            // Quick query buttons
            var quickStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            quickStack.Children.Add(new TextBlock
            {
                Text = "Quick:",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
            });

            var parts = new[] { "Bumper", "Fender", "Door", "Hood", "Quarter Panel" };
            foreach (var part in parts)
            {
                var btn = new Button
                {
                    Content = part,
                    Tag = part,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    Padding = new Thickness(10, 5, 10, 5)
                };
                btn.Click += QuickQuery_Click;
                quickStack.Children.Add(btn);
            }
            panel.Children.Add(quickStack);

            // Results area
            _queryResult = new TextBlock
            {
                Text = "Enter a query above to search learned operations...",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            };
            panel.Children.Add(_queryResult);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        // ---- Insurance Payments sub-tab ----

        private ComboBox? _paymentsInsurerBox;
        private TextBox? _paymentsKeywordBox;
        private CheckBox? _paymentsIncludeDeniedBox;
        private TextBlock? _paymentsSummaryText;
        private ListView? _paymentsResultsList;
        private TextBlock? _paymentsStatusText;
        private InsurancePaymentReport? _paymentsCurrentReport;

        private const string AllInsurersLabel = "(All insurers)";

        private UIElement CreateInsurancePaymentsPanel()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Padding = new Thickness(20), Spacing = 12 };

            // Privacy banner
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 40, 40)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock
                {
                    Text = "Internal use only — contains insurance and claim data. Do not share with customers.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 210, 210)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            });

            // Description
            panel.Children.Add(new TextBlock
            {
                Text = "Search your own imported estimate history for how a specific insurer has paid for a given operation. Paste the results into your next estimate to push back against denials.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });

            // Controls row
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            _paymentsInsurerBox = new ComboBox
            {
                Width = 220,
                PlaceholderText = "Insurer",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            PopulateInsurerDropdown();
            controls.Children.Add(_paymentsInsurerBox);

            _paymentsKeywordBox = new TextBox
            {
                PlaceholderText = "Operation keyword (e.g. feather edge, corrosion)",
                Width = 360,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            _paymentsKeywordBox.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                    RunPaymentsSearch();
            };
            controls.Children.Add(_paymentsKeywordBox);

            var searchBtn = new Button
            {
                Content = "Search",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(16, 6, 16, 6)
            };
            searchBtn.Click += (s, e) => RunPaymentsSearch();
            controls.Children.Add(searchBtn);

            panel.Children.Add(controls);

            // Include denied toggle
            _paymentsIncludeDeniedBox = new CheckBox
            {
                Content = "Include denied lines (shown in red)",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                IsChecked = false
            };
            panel.Children.Add(_paymentsIncludeDeniedBox);

            // Summary banner
            _paymentsSummaryText = new TextBlock
            {
                Text = "Pick an insurer, type an operation keyword, and hit Search.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 50, 60)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 120, 160)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = _paymentsSummaryText
            });

            // Results list
            _paymentsResultsList = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                MinHeight = 250,
                MaxHeight = 480,
                SelectionMode = ListViewSelectionMode.None
            };
            panel.Children.Add(_paymentsResultsList);

            // Action buttons
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };

            var pdfBtn = new Button
            {
                Content = "Export Report PDF",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 100, 140)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(14, 6, 14, 6)
            };
            pdfBtn.Click += PaymentsPdf_Click;
            actions.Children.Add(pdfBtn);

            var copyBtn = new Button
            {
                Content = "Copy Summary to Clipboard",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 110, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(14, 6, 14, 6)
            };
            copyBtn.Click += PaymentsCopy_Click;
            actions.Children.Add(copyBtn);

            var clearBtn = new Button
            {
                Content = "Clear",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                Padding = new Thickness(14, 6, 14, 6)
            };
            clearBtn.Click += (s, e) => ClearPaymentsResults();
            actions.Children.Add(clearBtn);

            _paymentsStatusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0)
            };
            actions.Children.Add(_paymentsStatusText);

            panel.Children.Add(actions);

            scroll.Content = panel;
            return scroll;
        }

        private void PopulateInsurerDropdown()
        {
            if (_paymentsInsurerBox == null) return;
            _paymentsInsurerBox.Items.Clear();
            _paymentsInsurerBox.Items.Add(AllInsurersLabel);

            foreach (var insurer in EstimateHistoryDatabase.Instance.KnownInsurers
                .Where(i => !string.IsNullOrWhiteSpace(i) && !i.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i, StringComparer.OrdinalIgnoreCase))
            {
                _paymentsInsurerBox.Items.Add(insurer);
            }

            if (_paymentsInsurerBox.Items.Count > 0)
                _paymentsInsurerBox.SelectedIndex = 0;
        }

        private void RunPaymentsSearch()
        {
            if (_paymentsInsurerBox == null || _paymentsKeywordBox == null || _paymentsResultsList == null ||
                _paymentsSummaryText == null || _paymentsStatusText == null)
                return;

            var keyword = _paymentsKeywordBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(keyword))
            {
                _paymentsStatusText.Text = "Enter an operation keyword first.";
                _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 160, 100));
                return;
            }

            var selectedInsurer = _paymentsInsurerBox.SelectedItem as string ?? AllInsurersLabel;
            var isAll = selectedInsurer == AllInsurersLabel;
            var includeDenied = _paymentsIncludeDeniedBox?.IsChecked == true;

            var report = InsurancePaymentReportService.Instance.Build(
                isAll ? "" : selectedInsurer, keyword, includeDenied);
            _paymentsCurrentReport = report;

            RenderPaymentsSummary(report);
            RenderPaymentsRows(report);

            _paymentsStatusText.Text = $"{report.Rows.Count} row(s).";
            _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150));
        }

        private void RenderPaymentsSummary(InsurancePaymentReport report)
        {
            if (_paymentsSummaryText == null) return;

            if (report.Rows.Count == 0)
            {
                _paymentsSummaryText.Text =
                    $"No history found for \"{report.OperationKeyword}\" on {report.Insurer}. " +
                    "Import more estimates to grow this dataset.";
                return;
            }

            var paidCount = Math.Max(report.Summary.TimesPaid, report.Rows.Count(r => r.WasPaid));
            var avg = report.Summary.AverageHours;

            var dateRange = report.EarliestDate.HasValue && report.LatestDate.HasValue
                ? $"{report.EarliestDate:MM/dd/yy} – {report.LatestDate:MM/dd/yy}"
                : "";

            var summary = $"{report.Insurer} paid for \"{report.OperationKeyword}\" " +
                          $"{paidCount} time{(paidCount == 1 ? "" : "s")} across " +
                          $"{report.UniqueEstimateCount} estimate{(report.UniqueEstimateCount == 1 ? "" : "s")}.";
            if (avg > 0)
                summary += $" Avg: {avg:0.##} hr labor";
            if (report.Summary.TotalAmountPaid > 0)
                summary += $", ${report.Summary.TotalAmountPaid:N2} total";
            if (!string.IsNullOrEmpty(dateRange))
                summary += $". Date range: {dateRange}";
            else
                summary += ".";

            _paymentsSummaryText.Text = summary;
        }

        private void RenderPaymentsRows(InsurancePaymentReport report)
        {
            if (_paymentsResultsList == null) return;
            _paymentsResultsList.Items.Clear();

            // Header row
            var headerGrid = BuildPaymentsRowGrid(
                "Date", "Claim #", "RO #", "Vehicle", "Operation", "Op", "Hrs", "Refin", "$",
                isHeader: true, isDenied: false);
            _paymentsResultsList.Items.Add(headerGrid);

            foreach (var row in report.Rows)
            {
                var desc = string.IsNullOrWhiteSpace(row.Description) ? (row.OperationType ?? "") : row.Description;
                if (desc.Length > 70) desc = desc.Substring(0, 67) + "...";

                var grid = BuildPaymentsRowGrid(
                    row.Date.ToString("MM/dd/yy"),
                    string.IsNullOrWhiteSpace(row.ClaimNumber) ? "—" : row.ClaimNumber,
                    string.IsNullOrWhiteSpace(row.RONumber) ? "—" : row.RONumber,
                    row.VehicleInfo ?? "",
                    desc,
                    row.OperationType ?? "",
                    row.LaborHours > 0 ? row.LaborHours.ToString("0.##") : "—",
                    row.RefinishHours > 0 ? row.RefinishHours.ToString("0.##") : "—",
                    row.Price > 0 ? $"${row.Price:0.##}" : "—",
                    isHeader: false,
                    isDenied: !row.WasPaid);
                _paymentsResultsList.Items.Add(grid);
            }

            if (report.Rows.Count == 0)
            {
                _paymentsResultsList.Items.Add(new TextBlock
                {
                    Text = "No matching line items.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(8)
                });
            }
        }

        private Grid BuildPaymentsRowGrid(
            string date, string claim, string ro, string vehicle, string operation,
            string op, string hrs, string refin, string price,
            bool isHeader, bool isDenied)
        {
            var grid = new Grid { Padding = new Thickness(4, 2, 4, 2) };
            // 9 columns with widths matching the PDF roughly
            double[] widths = { 60, 90, 55, 150, 260, 45, 50, 50, 60 };
            foreach (var w in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            var values = new[] { date, claim, ro, vehicle, operation, op, hrs, refin, price };
            Windows.UI.Color color;
            if (isHeader) color = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            else if (isDenied) color = Windows.UI.Color.FromArgb(255, 230, 120, 120);
            else color = Windows.UI.Color.FromArgb(255, 210, 210, 210);

            for (int i = 0; i < values.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = values[i],
                    Foreground = new SolidColorBrush(color),
                    FontSize = 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
            return grid;
        }

        private void ClearPaymentsResults()
        {
            _paymentsCurrentReport = null;
            _paymentsResultsList?.Items.Clear();
            if (_paymentsSummaryText != null)
                _paymentsSummaryText.Text = "Pick an insurer, type an operation keyword, and hit Search.";
            if (_paymentsStatusText != null)
                _paymentsStatusText.Text = "";
            if (_paymentsKeywordBox != null)
                _paymentsKeywordBox.Text = "";
        }

        private void PaymentsCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_paymentsCurrentReport == null || _paymentsCurrentReport.Rows.Count == 0)
            {
                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = "Nothing to copy — run a search first.";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 160, 100));
                }
                return;
            }

            try
            {
                var text = InsurancePaymentReportService.Instance.GenerateClipboardText(_paymentsCurrentReport);
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);

                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = "Summary copied.";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150));
                }
            }
            catch (Exception ex)
            {
                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = $"Copy failed: {ex.Message}";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 120, 120));
                }
            }
        }

        private void PaymentsPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_paymentsCurrentReport == null || _paymentsCurrentReport.Rows.Count == 0)
            {
                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = "Nothing to export — run a search first.";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 160, 100));
                }
                return;
            }

            try
            {
                var path = InsurancePaymentReportService.Instance.GeneratePdf(_paymentsCurrentReport);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = "PDF opened.";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150));
                }
            }
            catch (Exception ex)
            {
                if (_paymentsStatusText != null)
                {
                    _paymentsStatusText.Text = $"PDF export failed: {ex.Message}";
                    _paymentsStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 120, 120));
                }
            }
        }

        // ---- Estimates Browser sub-tab ----

        private TextBox? _browserFilterBox;
        private TextBlock? _browserCountText;
        private ListView? _browserList;

        // Structured filter controls
        private ComboBox? _browserInsurerBox;
        private ComboBox? _browserYearBox;
        private ComboBox? _browserMakeBox;
        private ComboBox? _browserModelBox;
        private ComboBox? _browserShopBox;
        private ComboBox? _browserSourceBox;
        private TextBox? _browserVinBox;
        private TextBox? _browserClaimBox;
        private TextBox? _browserRoBox;
        private TextBox? _browserOperationBox;

        private const string AnyLabel = "(Any)";

        private UIElement CreateEstimatesBrowserPanel()
        {
            var grid = new Grid { Padding = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // privacy
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // quick filter
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // dropdowns
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // text filters
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // count
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list

            // Privacy banner
            var privacy = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 40, 40)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = "Internal use only — contains insurance and claim data. Do not share with customers.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 210, 210)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetRow(privacy, 0);
            grid.Children.Add(privacy);

            // Quick filter row
            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
            filterRow.Children.Add(new TextBlock
            {
                Text = "Search:",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            });

            _browserFilterBox = new TextBox
            {
                PlaceholderText = "Free-text across insurer, shop, RO #, claim #, vehicle, VIN, customer, adjuster...",
                Width = 520,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            _browserFilterBox.TextChanged += (s, e) => RefreshBrowserList();
            filterRow.Children.Add(_browserFilterBox);

            var clearBtn = new Button
            {
                Content = "Clear All",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
            };
            clearBtn.Click += (s, e) => ClearBrowserFilters();
            filterRow.Children.Add(clearBtn);

            var refreshBtn = new Button
            {
                Content = "Refresh",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
            };
            refreshBtn.Click += (s, e) => { PopulateBrowserDropdowns(); RefreshBrowserList(); };
            filterRow.Children.Add(refreshBtn);

            Grid.SetRow(filterRow, 1);
            grid.Children.Add(filterRow);

            // Dropdown filters row
            var dropRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };

            _browserInsurerBox = BuildBrowserDropdown("Insurer", 180);
            _browserYearBox = BuildBrowserDropdown("Year", 90);
            _browserMakeBox = BuildBrowserDropdown("Make", 130);
            _browserModelBox = BuildBrowserDropdown("Model", 150);
            _browserShopBox = BuildBrowserDropdown("Shop", 170);
            _browserSourceBox = BuildBrowserDropdown("Source", 110);

            // Cascade: when Make changes, repopulate Model
            _browserMakeBox.SelectionChanged += (s, e) =>
            {
                PopulateModelDropdown();
                RefreshBrowserList();
            };

            dropRow.Children.Add(LabeledControl("Insurer", _browserInsurerBox));
            dropRow.Children.Add(LabeledControl("Year", _browserYearBox));
            dropRow.Children.Add(LabeledControl("Make", _browserMakeBox));
            dropRow.Children.Add(LabeledControl("Model", _browserModelBox));
            dropRow.Children.Add(LabeledControl("Shop", _browserShopBox));
            dropRow.Children.Add(LabeledControl("Source", _browserSourceBox));

            Grid.SetRow(dropRow, 2);
            grid.Children.Add(dropRow);

            // Text filters row
            var textRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };

            _browserVinBox = BuildBrowserTextFilter("VIN (full or partial)", 180);
            _browserClaimBox = BuildBrowserTextFilter("Claim #", 130);
            _browserRoBox = BuildBrowserTextFilter("RO #", 110);
            _browserOperationBox = BuildBrowserTextFilter("Operation keyword (e.g. feather edge)", 260);

            textRow.Children.Add(LabeledControl("VIN", _browserVinBox));
            textRow.Children.Add(LabeledControl("Claim #", _browserClaimBox));
            textRow.Children.Add(LabeledControl("RO #", _browserRoBox));
            textRow.Children.Add(LabeledControl("Operation", _browserOperationBox));

            Grid.SetRow(textRow, 3);
            grid.Children.Add(textRow);

            _browserCountText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(_browserCountText, 4);
            grid.Children.Add(_browserCountText);

            // Column header row
            var header = BuildBrowserRow(
                "Date", "Insurer", "Shop", "RO #", "Claim #", "Vehicle", "Total", "Quality",
                isHeader: true);
            header.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetRow(header, 5);
            grid.Children.Add(header);

            _browserList = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                SelectionMode = ListViewSelectionMode.None
            };
            Grid.SetRow(_browserList, 6);
            grid.Children.Add(_browserList);

            PopulateBrowserDropdowns();
            RefreshBrowserList();
            return grid;
        }

        private StackPanel LabeledControl(string label, FrameworkElement control)
        {
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
            });
            sp.Children.Add(control);
            return sp;
        }

        private ComboBox BuildBrowserDropdown(string name, double width)
        {
            var box = new ComboBox
            {
                Width = width,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            box.SelectionChanged += (s, e) =>
            {
                // Skip — the Make dropdown has its own handler that also refreshes.
                if (!ReferenceEquals(box, _browserMakeBox))
                    RefreshBrowserList();
            };
            return box;
        }

        private TextBox BuildBrowserTextFilter(string placeholder, double width)
        {
            var tb = new TextBox
            {
                PlaceholderText = placeholder,
                Width = width,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            tb.TextChanged += (s, e) => RefreshBrowserList();
            return tb;
        }

        private void PopulateBrowserDropdowns()
        {
            var estimates = EstimateHistoryDatabase.Instance.AllEstimates;

            // Insurer
            PopulateDropdown(_browserInsurerBox, estimates
                .Select(e => e.InsuranceCompany)
                .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));

            // Year
            PopulateDropdown(_browserYearBox, estimates
                .Select(e => ParseYear(e.VehicleInfo))
                .Where(y => y > 0)
                .Distinct()
                .OrderByDescending(y => y)
                .Select(y => y.ToString()));

            // Make
            PopulateDropdown(_browserMakeBox, estimates
                .Select(e => ParseMake(e.VehicleInfo))
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase));

            PopulateModelDropdown();

            // Shop
            PopulateDropdown(_browserShopBox, estimates
                .Select(e => e.ShopName)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));

            // Source system
            PopulateDropdown(_browserSourceBox, estimates
                .Select(e => e.EstimateSource)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
        }

        private void PopulateModelDropdown()
        {
            if (_browserModelBox == null) return;
            var selectedMake = _browserMakeBox?.SelectedItem as string;
            var estimates = EstimateHistoryDatabase.Instance.AllEstimates;

            IEnumerable<string> models;
            if (string.IsNullOrWhiteSpace(selectedMake) || selectedMake == AnyLabel)
            {
                models = estimates
                    .Select(e => ParseModel(e.VehicleInfo))
                    .Where(m => !string.IsNullOrWhiteSpace(m));
            }
            else
            {
                models = estimates
                    .Where(e => string.Equals(ParseMake(e.VehicleInfo), selectedMake, StringComparison.OrdinalIgnoreCase))
                    .Select(e => ParseModel(e.VehicleInfo))
                    .Where(m => !string.IsNullOrWhiteSpace(m));
            }

            PopulateDropdown(_browserModelBox, models
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase));
        }

        private static void PopulateDropdown(ComboBox? box, IEnumerable<string> values)
        {
            if (box == null) return;
            var prev = box.SelectedItem as string;
            box.Items.Clear();
            box.Items.Add(AnyLabel);
            foreach (var v in values)
                box.Items.Add(v);
            // Restore prior selection when possible
            if (!string.IsNullOrEmpty(prev) && box.Items.Contains(prev))
                box.SelectedItem = prev;
            else
                box.SelectedIndex = 0;
        }

        private void ClearBrowserFilters()
        {
            if (_browserFilterBox != null) _browserFilterBox.Text = "";
            if (_browserVinBox != null) _browserVinBox.Text = "";
            if (_browserClaimBox != null) _browserClaimBox.Text = "";
            if (_browserRoBox != null) _browserRoBox.Text = "";
            if (_browserOperationBox != null) _browserOperationBox.Text = "";
            foreach (var cb in new[] { _browserInsurerBox, _browserYearBox, _browserMakeBox,
                                        _browserModelBox, _browserShopBox, _browserSourceBox })
            {
                if (cb != null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            }
            RefreshBrowserList();
        }

        private void RefreshBrowserList()
        {
            if (_browserList == null) return;
            _browserList.Items.Clear();

            var estimates = EstimateHistoryDatabase.Instance.AllEstimates;

            var filtered = estimates.Where(EstimateMatchesAllFilters).ToList();
            var sorted = filtered.OrderByDescending(e => e.ImportedDate).ToList();

            foreach (var est in sorted)
            {
                var total = est.GrandTotal > 0 ? $"${est.GrandTotal:N0}" : "—";
                var quality = string.IsNullOrWhiteSpace(est.QualityGrade)
                    ? "—"
                    : $"{est.QualityGrade} ({est.QualityScore})";
                var row = BuildBrowserRow(
                    est.ImportedDate.ToString("MM/dd/yy"),
                    string.IsNullOrWhiteSpace(est.InsuranceCompany) ? "—" : est.InsuranceCompany,
                    string.IsNullOrWhiteSpace(est.ShopName) ? "—" : est.ShopName,
                    string.IsNullOrWhiteSpace(est.RONumber) ? "—" : est.RONumber,
                    string.IsNullOrWhiteSpace(est.ClaimNumber) ? "—" : est.ClaimNumber,
                    string.IsNullOrWhiteSpace(est.VehicleInfo) ? "—" : est.VehicleInfo,
                    total,
                    quality,
                    isHeader: false);
                row.Tag = est;
                row.PointerPressed += BrowserRow_PointerPressed;
                _browserList.Items.Add(row);
            }

            if (_browserCountText != null)
            {
                var anyFilterActive = IsAnyFilterActive();
                _browserCountText.Text = anyFilterActive
                    ? $"{sorted.Count} of {estimates.Count} estimate{(estimates.Count == 1 ? "" : "s")} match active filters"
                    : $"{sorted.Count} estimate{(sorted.Count == 1 ? "" : "s")}";
            }

            if (sorted.Count == 0)
            {
                _browserList.Items.Add(new TextBlock
                {
                    Text = estimates.Count == 0
                        ? "No estimates imported yet. Use the Import Estimates tab."
                        : "No estimates match the current filters.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(8)
                });
            }
        }

        private bool IsAnyFilterActive()
        {
            bool TextActive(TextBox? tb) => !string.IsNullOrWhiteSpace(tb?.Text);
            bool DropActive(ComboBox? cb) => cb?.SelectedItem is string s && s != AnyLabel;
            return TextActive(_browserFilterBox) || TextActive(_browserVinBox) ||
                   TextActive(_browserClaimBox) || TextActive(_browserRoBox) ||
                   TextActive(_browserOperationBox) ||
                   DropActive(_browserInsurerBox) || DropActive(_browserYearBox) ||
                   DropActive(_browserMakeBox) || DropActive(_browserModelBox) ||
                   DropActive(_browserShopBox) || DropActive(_browserSourceBox);
        }

        private bool EstimateMatchesAllFilters(StoredEstimate est)
        {
            // Dropdown filters
            if (_browserInsurerBox?.SelectedItem is string ins && ins != AnyLabel &&
                !string.Equals(est.InsuranceCompany, ins, StringComparison.OrdinalIgnoreCase))
                return false;

            if (_browserYearBox?.SelectedItem is string yr && yr != AnyLabel)
            {
                if (ParseYear(est.VehicleInfo).ToString() != yr)
                    return false;
            }

            if (_browserMakeBox?.SelectedItem is string mk && mk != AnyLabel &&
                !string.Equals(ParseMake(est.VehicleInfo), mk, StringComparison.OrdinalIgnoreCase))
                return false;

            if (_browserModelBox?.SelectedItem is string md && md != AnyLabel &&
                !string.Equals(ParseModel(est.VehicleInfo), md, StringComparison.OrdinalIgnoreCase))
                return false;

            if (_browserShopBox?.SelectedItem is string sh && sh != AnyLabel &&
                !string.Equals(est.ShopName, sh, StringComparison.OrdinalIgnoreCase))
                return false;

            if (_browserSourceBox?.SelectedItem is string src && src != AnyLabel &&
                !string.Equals(est.EstimateSource, src, StringComparison.OrdinalIgnoreCase))
                return false;

            // Text filters (partial, case-insensitive)
            bool ContainsCI(string? field, string needle) =>
                !string.IsNullOrEmpty(field) && field.Contains(needle, StringComparison.OrdinalIgnoreCase);

            var vin = _browserVinBox?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(vin) && !ContainsCI(est.VIN, vin)) return false;

            var claim = _browserClaimBox?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(claim) && !ContainsCI(est.ClaimNumber, claim)) return false;

            var ro = _browserRoBox?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(ro) && !ContainsCI(est.RONumber, ro)) return false;

            var op = _browserOperationBox?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(op))
            {
                var hasMatch = est.LineItems.Any(li =>
                    ContainsCI(li.Description, op) ||
                    ContainsCI(li.OperationType, op) ||
                    ContainsCI(li.PartName, op));
                if (!hasMatch) return false;
            }

            // Free-text search across common fields
            var free = _browserFilterBox?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(free))
            {
                if (!(ContainsCI(est.InsuranceCompany, free) ||
                      ContainsCI(est.ShopName, free) ||
                      ContainsCI(est.RONumber, free) ||
                      ContainsCI(est.ClaimNumber, free) ||
                      ContainsCI(est.VehicleInfo, free) ||
                      ContainsCI(est.VIN, free) ||
                      ContainsCI(est.EstimateSource, free) ||
                      ContainsCI(est.CustomerName, free) ||
                      ContainsCI(est.AdjusterName, free)))
                    return false;
            }

            return true;
        }

        // --- Vehicle info parsing (runtime, no schema change) ---

        /// <summary>Parse "2019 Honda Accord LX" → 2019. Returns 0 if not found.</summary>
        internal static int ParseYear(string? vehicleInfo)
        {
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(vehicleInfo, @"\b(19|20)\d{2}\b");
            if (m.Success && int.TryParse(m.Value, out var year))
                return year;
            return 0;
        }

        /// <summary>Parse "2019 Honda Accord LX" → "Honda". Returns "" if not found.</summary>
        internal static string ParseMake(string? vehicleInfo)
        {
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return "";
            // Strip leading year, then the next token is the make
            var cleaned = System.Text.RegularExpressions.Regex.Replace(vehicleInfo.Trim(), @"^(19|20)\d{2}\s+", "");
            var tokens = cleaned.Split(new[] { ' ', '\t', '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "";
            return NormalizeCase(tokens[0]);
        }

        /// <summary>Parse "2019 Honda Accord LX" → "Accord". Returns "" if not found.</summary>
        internal static string ParseModel(string? vehicleInfo)
        {
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return "";
            var cleaned = System.Text.RegularExpressions.Regex.Replace(vehicleInfo.Trim(), @"^(19|20)\d{2}\s+", "");
            var tokens = cleaned.Split(new[] { ' ', '\t', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return "";
            return NormalizeCase(tokens[1]);
        }

        private static string NormalizeCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var hasLower = s.Any(char.IsLower);
            var hasUpper = s.Any(char.IsUpper);
            if (hasLower && hasUpper) return s;
            if (s.Length <= 3) return s.ToUpperInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private void BrowserRow_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid g && g.Tag is StoredEstimate est)
            {
                ShowEstimateDetailDialog(est);
            }
        }

        private async void ShowEstimateDetailDialog(StoredEstimate est)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Imported: {est.ImportedDate:g}");
            sb.AppendLine($"Source:   {est.EstimateSource}");
            sb.AppendLine($"Shop:     {(string.IsNullOrWhiteSpace(est.ShopName) ? "(not captured)" : est.ShopName)}");
            sb.AppendLine($"Insurer:  {est.InsuranceCompany}");
            sb.AppendLine($"RO #:     {est.RONumber}");
            sb.AppendLine($"Claim #:  {est.ClaimNumber}");
            sb.AppendLine($"Vehicle:  {est.VehicleInfo}");
            sb.AppendLine($"VIN:      {est.VIN}");
            sb.AppendLine($"Total:    ${est.GrandTotal:N2}");
            sb.AppendLine($"Quality:  {est.QualityGrade} ({est.QualityScore}/100)");
            sb.AppendLine($"Lines:    {est.LineItems.Count}");
            if (!string.IsNullOrWhiteSpace(est.SourceFile))
                sb.AppendLine($"File:     {est.SourceFile}");

            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock
            {
                Text = sb.ToString(),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = $"Estimate Detail",
                Content = body,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };

            if (!string.IsNullOrWhiteSpace(est.SourceFile) && System.IO.File.Exists(est.SourceFile))
            {
                dialog.PrimaryButtonText = "Open Source PDF";
                dialog.DefaultButton = ContentDialogButton.Primary;
            }

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary &&
                !string.IsNullOrWhiteSpace(est.SourceFile) &&
                System.IO.File.Exists(est.SourceFile))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = est.SourceFile, UseShellExecute = true });
                }
                catch { /* swallow — dialog has already closed */ }
            }
        }

        private Grid BuildBrowserRow(
            string date, string insurer, string shop, string ro, string claim,
            string vehicle, string total, string quality, bool isHeader)
        {
            var grid = new Grid { Padding = new Thickness(6, 4, 6, 4) };
            // 8 columns
            double[] widths = { 70, 130, 170, 70, 110, 210, 80, 100 };
            foreach (var w in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            var values = new[] { date, insurer, shop, ro, claim, vehicle, total, quality };
            var color = isHeader
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 210, 210, 210);

            for (int i = 0; i < values.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = values[i],
                    Foreground = new SolidColorBrush(color),
                    FontSize = 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
            return grid;
        }

        // ---- By Insurer sub-tab ----

        private StackPanel? _byInsurerStack;
        private TextBlock? _byInsurerSummary;

        private UIElement CreateByInsurerPanel()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var outer = new StackPanel { Padding = new Thickness(20), Spacing = 12 };

            // Privacy banner
            outer.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 40, 40)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock
                {
                    Text = "Internal use only — contains insurance and claim data. Do not share with customers.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 210, 210)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            });

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            _byInsurerSummary = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(_byInsurerSummary);

            var refreshBtn = new Button
            {
                Content = "Refresh",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
            };
            refreshBtn.Click += (s, e) => RefreshByInsurerGroups();
            topRow.Children.Add(refreshBtn);

            outer.Children.Add(topRow);

            _byInsurerStack = new StackPanel { Spacing = 6 };
            outer.Children.Add(_byInsurerStack);

            RefreshByInsurerGroups();
            scroll.Content = outer;
            return scroll;
        }

        private void RefreshByInsurerGroups()
        {
            if (_byInsurerStack == null) return;
            _byInsurerStack.Children.Clear();

            var estimates = EstimateHistoryDatabase.Instance.AllEstimates;
            var groups = estimates
                .GroupBy(e => string.IsNullOrWhiteSpace(e.InsuranceCompany) ? "Unknown" : e.InsuranceCompany,
                         StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_byInsurerSummary != null)
            {
                _byInsurerSummary.Text = estimates.Count == 0
                    ? "No estimates imported yet. Use the Import Estimates tab to get started."
                    : $"{estimates.Count} estimate{(estimates.Count == 1 ? "" : "s")} across " +
                      $"{groups.Count} insurer{(groups.Count == 1 ? "" : "s")}.";
            }

            foreach (var g in groups)
            {
                var count = g.Count();
                var total = g.Sum(e => e.GrandTotal);
                var dateRange = g.Any()
                    ? $"{g.Min(e => e.ImportedDate):MM/dd/yy} – {g.Max(e => e.ImportedDate):MM/dd/yy}"
                    : "";

                var headerText = new TextBlock
                {
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    TextWrapping = TextWrapping.NoWrap
                };
                headerText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = g.Key });
                headerText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = $"   —   {count} estimate{(count == 1 ? "" : "s")}" +
                           (total > 0 ? $", ${total:N0} total" : "") +
                           $"   ·   {dateRange}",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 180, 210))
                });

                var contentStack = new StackPanel { Spacing = 2, Padding = new Thickness(4) };

                // Column header within the group
                contentStack.Children.Add(BuildInsurerGroupRow(
                    "Date", "Shop", "RO #", "Claim #", "Vehicle", "Total",
                    isHeader: true));

                foreach (var est in g.OrderByDescending(e => e.ImportedDate))
                {
                    var row = BuildInsurerGroupRow(
                        est.ImportedDate.ToString("MM/dd/yy"),
                        string.IsNullOrWhiteSpace(est.ShopName) ? "—" : est.ShopName,
                        string.IsNullOrWhiteSpace(est.RONumber) ? "—" : est.RONumber,
                        string.IsNullOrWhiteSpace(est.ClaimNumber) ? "—" : est.ClaimNumber,
                        string.IsNullOrWhiteSpace(est.VehicleInfo) ? "—" : est.VehicleInfo,
                        est.GrandTotal > 0 ? $"${est.GrandTotal:N0}" : "—",
                        isHeader: false);
                    row.Tag = est;
                    row.PointerPressed += BrowserRow_PointerPressed;
                    contentStack.Children.Add(row);
                }

                var expander = new Expander
                {
                    Header = headerText,
                    Content = contentStack,
                    IsExpanded = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 45, 55))
                };
                _byInsurerStack.Children.Add(expander);
            }
        }

        private Grid BuildInsurerGroupRow(
            string date, string shop, string ro, string claim, string vehicle, string total,
            bool isHeader)
        {
            var grid = new Grid { Padding = new Thickness(4, 3, 4, 3) };
            double[] widths = { 70, 180, 80, 120, 230, 90 };
            foreach (var w in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

            var values = new[] { date, shop, ro, claim, vehicle, total };
            var color = isHeader
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 210, 210, 210);

            for (int i = 0; i < values.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = values[i],
                    Foreground = new SolidColorBrush(color),
                    FontSize = 12,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
            return grid;
        }

        private UIElement CreateImportPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Instructions
            panel.Children.Add(new TextBlock
            {
                Text = "Import estimate PDFs to train the learning system",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                FontSize = 14
            });

            // Import button
            var importStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            var importButton = new Button
            {
                Content = "Select PDF to Import",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(20, 10, 20, 10)
            };
            importButton.Click += ImportButton_Click;
            importStack.Children.Add(importButton);

            _importProgress = new ProgressRing
            {
                IsActive = false,
                Width = 24,
                Height = 24
            };
            importStack.Children.Add(_importProgress);

            panel.Children.Add(importStack);

            // Status text
            _importStatusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_importStatusText);

            // Parsed items preview
            panel.Children.Add(new TextBlock
            {
                Text = "Parsed Line Items:",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 5)
            });

            _parsedItemsListView = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                MaxHeight = 300,
                SelectionMode = ListViewSelectionMode.None
            };
            panel.Children.Add(_parsedItemsListView);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private TextBlock? _miningStatusText;
        private ProgressRing? _miningProgress;

        private UIElement CreateStatsPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Stats display
            _statsText = new TextBlock
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_statsText);

            // Mining section header
            var miningHeader = new TextBlock
            {
                Text = "PATTERN MINING ENGINE",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255)),
                Margin = new Thickness(0, 30, 0, 10)
            };
            panel.Children.Add(miningHeader);

            var miningDesc = new TextBlock
            {
                Text = "Run a full mining pass to analyze ALL uploaded estimates and extract patterns.\nThis improves ghost estimates, part recognition, and suggestions.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(miningDesc);

            // Mining status and progress
            var miningStatusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            _miningProgress = new ProgressRing
            {
                Width = 20,
                Height = 20,
                IsActive = false,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255))
            };
            miningStatusPanel.Children.Add(_miningProgress);

            _miningStatusText = new TextBlock
            {
                Text = "Ready",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            miningStatusPanel.Children.Add(_miningStatusText);
            panel.Children.Add(miningStatusPanel);

            // Run Mining Pass button
            var miningButton = new Button
            {
                Content = "Run Full Mining Pass",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 100, 140)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 10, 0, 0)
            };
            miningButton.Click += RunMiningPass_Click;
            panel.Children.Add(miningButton);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                Margin = new Thickness(0, 20, 0, 10)
            };
            panel.Children.Add(separator);

            // Button row
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            // Refresh button
            var refreshButton = new Button
            {
                Content = "Refresh Statistics",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
            };
            refreshButton.Click += (s, e) => RefreshStats();
            buttonRow.Children.Add(refreshButton);

            // Clear data button (dangerous)
            var clearButton = new Button
            {
                Content = "Clear All Learned Data",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 50, 50)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 200))
            };
            clearButton.Click += ClearData_Click;
            buttonRow.Children.Add(clearButton);

            panel.Children.Add(buttonRow);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private async void RunMiningPass_Click(object sender, RoutedEventArgs e)
        {
            if (EstimateMiningEngine.Instance.IsMining)
            {
                _miningStatusText!.Text = "Mining already in progress...";
                return;
            }

            _miningProgress!.IsActive = true;
            _miningStatusText!.Text = "Starting mining pass...";
            _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255));

            // Subscribe to progress updates
            EstimateMiningEngine.Instance.ProgressChanged += (s, progress) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _miningStatusText.Text = $"{progress.Phase}: {progress.ProcessedEstimates}/{progress.TotalEstimates} ({progress.PercentComplete:F0}%)";
                });
            };

            try
            {
                var result = await EstimateMiningEngine.Instance.RunFullMiningPassAsync();

                _miningProgress.IsActive = false;
                if (result.Success)
                {
                    _miningStatusText.Text = $"Complete! Learned {result.PartsLearned} parts, {result.PatternsDiscovered} patterns in {result.Duration.TotalSeconds:F1}s";
                    _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 255, 100));
                }
                else
                {
                    _miningStatusText.Text = $"Failed: {result.Message}";
                    _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
                }

                // Refresh stats to show new data
                RefreshStats();
            }
            catch (Exception ex)
            {
                _miningProgress.IsActive = false;
                _miningStatusText.Text = $"Error: {ex.Message}";
                _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
            }
        }

        private UIElement CreatePatternsPanel()
        {
            var panel = new Grid
            {
                Padding = new Thickness(20)
            };

            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var headerText = new TextBlock
            {
                Text = "Learned Patterns (sorted by confidence)",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(headerText, 0);
            panel.Children.Add(headerText);

            // Patterns list
            _patternsListView = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                SelectionMode = ListViewSelectionMode.None
            };
            Grid.SetRow(_patternsListView, 1);
            panel.Children.Add(_patternsListView);

            return panel;
        }

        private UIElement CreateTipsPanel()
        {
            var mainScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20)
            };

            var panel = new StackPanel { Spacing = 20 };

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "Estimating Tips & Tricks",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130))
            });

            // Photo Documentation Section
            panel.Children.Add(CreateTipSection(
                "Photo Documentation: Telling the Story to Insurers",
                "By Anthony McNee",
                new List<TipItem>
                {
                    new TipItem
                    {
                        Title = "Photos as Visual Illustrations",
                        Content = "Photos should TELL A STORY, not just document. Anyone reviewing them (especially adjusters) should instantly understand what repairs are needed and their severity.",
                        Icon = "\uE722" // Camera
                    },
                    new TipItem
                    {
                        Title = "Lighting is Critical",
                        Content = "Proper lighting reveals damage depth and detail that might otherwise be missed entirely. Poor lighting = missed damage = reduced labor hours. Position lights to show dents, scratches, and texture damage clearly.",
                        Icon = "\uE706" // Brightness
                    },
                    new TipItem
                    {
                        Title = "Perspective & Context",
                        Content = "View photos from the adjuster's perspective - they may not know where a part originated. TIP: Put the removed part next to where you took it off the vehicle to show scale and location clearly.",
                        Icon = "\uE8B9" // View
                    },
                    new TipItem
                    {
                        Title = "Sequential Documentation",
                        Content = "Photos should follow a logical progression. Create a coherent narrative: Overall damage → Close-up details → Hidden damage → Repair process → Completed work.",
                        Icon = "\uE8FD" // List
                    },
                    new TipItem
                    {
                        Title = "Use Visual Aid Tools",
                        Content = "Use dent boards for scale comparison. Use COLOR-CODED POINTER STICKS:\n• One color for REPAIR operations\n• Different color for REPLACE operations\nThis makes it instantly clear what work is required.",
                        Icon = "\uE790" // Highlight
                    },
                    new TipItem
                    {
                        Title = "Document Before Disassembly",
                        Content = "Always photograph damage BEFORE removing parts. Once disassembled, the original damage context is lost. Take wide shots and close-ups of each damaged area.",
                        Icon = "\uE7C5" // Shield
                    },
                    new TipItem
                    {
                        Title = "Get ALL Info While Vehicle is Present",
                        Content = "Don't let the car leave without capturing EVERYTHING:\n• Check for ADAS equipment (cameras, sensors, radar)\n• Walk around entire vehicle for prior/unrelated damage\n• Get it on the measuring system\n• Check and document the paint code\n• Verify VIN and options\n• Check tire sizes and condition\n• Look for aftermarket parts/modifications\n\nYou can't supplement what you didn't document!",
                        Icon = "\uE762" // Car
                    }
                }
            ));

            // Photo Checklist
            panel.Children.Add(CreateChecklistSection(
                "Photo Documentation Checklist",
                new List<string>
                {
                    "Overall vehicle photos (all 4 corners)",
                    "VIN plate / sticker photo",
                    "Odometer reading",
                    "Paint code label photo",
                    "Wide shot of damaged area",
                    "Close-up of each damage point",
                    "Measuring tape/scale reference for dents",
                    "Photos with dent board behind damage",
                    "Color-coded pointers indicating repair vs replace",
                    "Hidden damage revealed during teardown",
                    "Part numbers on OEM parts being replaced",
                    "Before/After comparison shots",
                    "ADAS components that need calibration"
                }
            ));

            // Vehicle Inspection Checklist
            panel.Children.Add(CreateChecklistSection(
                "While Vehicle is Present - Don't Miss!",
                new List<string>
                {
                    "Check ALL ADAS equipment (cameras, radar, sensors)",
                    "Walk around ENTIRE vehicle for prior/unrelated damage",
                    "Get vehicle on measuring system",
                    "Document paint code from label",
                    "Verify VIN matches paperwork",
                    "Check tire sizes and condition",
                    "Note any aftermarket parts or modifications",
                    "Check for fluid leaks",
                    "Test all lights and electrical",
                    "Document interior condition",
                    "Check for warning lights on dash",
                    "Note any unusual sounds or smells"
                }
            ));

            // Color Coding Guide
            panel.Children.Add(CreateColorGuideSection());

            // More Tips Coming Soon
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 50, 45)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = "More tips coming soon! Have a tip to share? Let us know.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                }
            });

            mainScroll.Content = panel;
            return mainScroll;
        }

        private Border CreateTipSection(string title, string author, List<TipItem> tips)
        {
            var sectionPanel = new StackPanel { Spacing = 12 };

            // Section header
            var headerStack = new StackPanel { Spacing = 4 };
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = author,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 180, 130)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
            sectionPanel.Children.Add(headerStack);

            // Tips
            foreach (var tip in tips)
            {
                var tipCard = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12)
                };

                var tipGrid = new Grid();
                tipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                tipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Icon
                var icon = new FontIcon
                {
                    Glyph = tip.Icon,
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130)),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(icon, 0);
                tipGrid.Children.Add(icon);

                // Content
                var contentStack = new StackPanel { Spacing = 4 };
                contentStack.Children.Add(new TextBlock
                {
                    Text = tip.Title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 220, 100))
                });
                contentStack.Children.Add(new TextBlock
                {
                    Text = tip.Content,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(contentStack, 1);
                tipGrid.Children.Add(contentStack);

                tipCard.Child = tipGrid;
                sectionPanel.Children.Add(tipCard);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = sectionPanel
            };
        }

        private Border CreateChecklistSection(string title, List<string> items)
        {
            var panel = new StackPanel { Spacing = 8 };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var item in items)
            {
                var itemStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                itemStack.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E", // Checkmark
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130))
                });
                itemStack.Children.Add(new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
                });
                panel.Children.Add(itemStack);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = panel
            };
        }

        private Border CreateColorGuideSection()
        {
            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(new TextBlock
            {
                Text = "Pointer Stick Color Coding Guide",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Color guide items
            var colors = new List<(string color, string hex, string meaning)>
            {
                ("RED", "#FF4444", "REPLACE - Part needs to be replaced"),
                ("YELLOW", "#FFCC00", "REPAIR - Part can be repaired"),
                ("BLUE", "#4488FF", "R&I - Remove and Install (for access)"),
                ("GREEN", "#44CC44", "BLEND - Adjacent panel needs blending"),
                ("ORANGE", "#FF8800", "SUPPLEMENT - Additional damage found")
            };

            foreach (var (color, hex, meaning) in colors)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Color swatch
                var swatch = new Border
                {
                    Width = 60,
                    Height = 24,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(ParseColor(hex)),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                swatch.Child = new TextBlock
                {
                    Text = color,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(swatch, 0);
                row.Children.Add(swatch);

                // Meaning
                var meaningText = new TextBlock
                {
                    Text = meaning,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(meaningText, 1);
                row.Children.Add(meaningText);

                panel.Children.Add(row);
            }

            panel.Children.Add(new TextBlock
            {
                Text = "Tip: Consistent color coding across your shop makes photos instantly understandable to anyone reviewing them.",
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = panel
            };
        }

        private Windows.UI.Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }

        private class TipItem
        {
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public string Icon { get; set; } = "\uE946"; // Default info icon
        }

        private void QueryButton_Click(object sender, RoutedEventArgs e)
        {
            var query = _queryInput.Text;
            if (string.IsNullOrWhiteSpace(query))
                return;

            ExecuteQuery(query);
        }

        private void QuickQuery_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string part)
            {
                _queryInput.Text = $"What operations do I need for a {part.ToLower()}?";
                ExecuteQuery($"repairing a {part.ToLower()}");
            }
        }

        private void ExecuteQuery(string query)
        {
            var learningService = EstimateLearningService.Instance;
            var result = learningService.QueryFromNaturalLanguage(query);

            var resultText = new System.Text.StringBuilder();
            resultText.AppendLine(result.Explanation);
            resultText.AppendLine();

            if (result.SuggestedOperations.Any())
            {
                resultText.AppendLine("SUGGESTED OPERATIONS:");
                resultText.AppendLine(new string('-', 50));

                foreach (var op in result.SuggestedOperations)
                {
                    resultText.AppendLine($"  {op.OperationType}: {op.Description}");
                    resultText.AppendLine($"    - Category: {op.Category}");
                    resultText.AppendLine($"    - Typical Labor: {op.TypicalLaborHours:F1} hrs");
                    if (op.TypicalRefinishHours > 0)
                        resultText.AppendLine($"    - Typical Refinish: {op.TypicalRefinishHours:F1} hrs");
                    if (op.TypicalPrice > 0)
                        resultText.AppendLine($"    - Typical Price: ${op.TypicalPrice:F2}");
                    resultText.AppendLine($"    - Confidence: {op.Confidence:P0}");
                    resultText.AppendLine($"    - Based on: {op.ExampleCount} examples");
                    resultText.AppendLine();
                }

                if (result.RelatedParts.Any())
                {
                    resultText.AppendLine();
                    resultText.AppendLine($"RELATED PARTS (often done together):");
                    resultText.AppendLine($"  {string.Join(", ", result.RelatedParts)}");
                }
            }
            else
            {
                resultText.AppendLine("No learned operations found. Import more estimates to build the database.");
            }

            _queryResult.Text = resultText.ToString();
            _queryResult.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220));
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pdf");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // Get the window handle for WinUI 3
            var hwnd = WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            _importProgress.IsActive = true;
            _importStatusText.Text = $"Parsing {file.Name}...";
            _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 100));
            _parsedItemsListView.Items.Clear();

            try
            {
                // Parse the PDF
                var parser = EstimatePdfParser.Instance;
                var estimate = parser.ParsePdf(file.Path);

                // Display parsed items
                foreach (var item in estimate.LineItems)
                {
                    var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.OperationType,
                        Width = 80,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.PartName,
                        Width = 150,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = $"{item.LaborHours:F1} hrs",
                        Width = 60,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.Price > 0 ? $"${item.Price:F2}" : "",
                        Width = 80,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.Description.Length > 50 ? item.Description.Substring(0, 50) + "..." : item.Description,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
                    });

                    _parsedItemsListView.Items.Add(itemPanel);
                }

                // Convert to training data and learn
                var trainingData = parser.ConvertToTrainingData(estimate);
                var learningService = EstimateLearningService.Instance;
                learningService.LearnFromEstimate(trainingData);

                // Auto-save to Estimate History Database for DNA/payment tracking
                try
                {
                    var historyDb = EstimateHistoryDatabase.Instance;
                    var estimateId = historyDb.AddEstimate(estimate);
                    System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Auto-saved estimate {estimateId} from LearningCenter");
                }
                catch (Exception historyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Failed to auto-save: {historyEx.Message}");
                }

                // Update status
                _importStatusText.Text = $"Successfully imported {estimate.LineItems.Count} line items from {file.Name}\n" +
                                          $"Source: {estimate.Source}\n" +
                                          $"Vehicle: {estimate.VehicleInfo}\n" +
                                          $"VIN: {estimate.VIN}";
                _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100));

                // Refresh stats
                RefreshStats();
            }
            catch (Exception ex)
            {
                _importStatusText.Text = $"Error importing PDF: {ex.Message}";
                _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
            }
            finally
            {
                _importProgress.IsActive = false;
            }
        }

        private async void ClearData_Click(object sender, RoutedEventArgs e)
        {
            // Only allow clearing personal data — never shop/baseline data
            if (LearningModeService.Instance.CurrentMode != LearningMode.Personal)
            {
                var blockedDialog = new ContentDialog
                {
                    Title = "Cannot Clear Shop Data",
                    Content = "Shop baseline data is read-only and cannot be cleared.\nSwitch to Personal mode to manage your own learned data.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                await blockedDialog.ShowAsync();
                return;
            }

            // First confirmation
            var confirmDialog = new ContentDialog
            {
                Title = "Clear Your Personal Learned Data?",
                Content = "This will delete all YOUR learned patterns and training examples.\nShop baseline data will not be affected.\n\nThis cannot be undone.",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var firstResult = await confirmDialog.ShowAsync();
            if (firstResult != ContentDialogResult.Primary)
                return;

            // Second confirmation — type CONFIRM
            var confirmInput = new TextBox
            {
                PlaceholderText = "Type CONFIRM to proceed",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 10, 0, 0)
            };

            var finalDialog = new ContentDialog
            {
                Title = "Type CONFIRM to Clear Data",
                Content = confirmInput,
                PrimaryButtonText = "Clear All Personal Data",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var finalResult = await finalDialog.ShowAsync();
            if (finalResult == ContentDialogResult.Primary && confirmInput.Text.Trim().Equals("CONFIRM", StringComparison.OrdinalIgnoreCase))
            {
                EstimateLearningService.Instance.ClearAllData();
                RefreshStats();
                _importStatusText.Text = "Personal learned data has been cleared.";
            }
        }

        private void RefreshStats()
        {
            var learningService = EstimateLearningService.Instance;
            var stats = learningService.GetStatistics();

            // Get mining engine stats
            var miningStats = EstimateMiningEngine.Instance.GetStatistics();
            var partStats = PartRecognitionEngine.Instance.GetStats();

            var statsBuilder = new System.Text.StringBuilder();
            statsBuilder.AppendLine($"LEARNING DATABASE STATISTICS");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Total Patterns Learned:     {stats.TotalPatterns}");
            statsBuilder.AppendLine($"Total Training Examples:    {stats.TotalExamples}");
            statsBuilder.AppendLine($"Estimates Processed:        {stats.TotalEstimatesTrained}");
            statsBuilder.AppendLine($"Average Pattern Confidence: {stats.AverageConfidence:P0}");
            statsBuilder.AppendLine();

            // Mining Engine Stats
            statsBuilder.AppendLine($"PATTERN MINING ENGINE");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Estimates Mined:            {miningStats.TotalEstimatesMined}");
            statsBuilder.AppendLine($"Lines Analyzed:             {miningStats.TotalLinesMined:N0}");
            statsBuilder.AppendLine($"Parts Learned:              {miningStats.TotalPartsLearned}");
            statsBuilder.AppendLine($"Part Aliases:               {miningStats.TotalAliasesLearned}");
            statsBuilder.AppendLine($"Co-occurrence Patterns:     {miningStats.TotalCoOccurrencePatterns}");
            statsBuilder.AppendLine($"Formulas Discovered:        {miningStats.TotalFormulasLearned}");
            statsBuilder.AppendLine($"Total Value Analyzed:       {miningStats.TotalValueMined:C0}");
            if (miningStats.LastMiningRun > DateTime.MinValue)
            {
                statsBuilder.AppendLine($"Last Updated:               {miningStats.LastMiningRun:g}");
            }
            statsBuilder.AppendLine();

            // Part Recognition Stats
            statsBuilder.AppendLine($"PART RECOGNITION ENGINE");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Canonical Parts:            {partStats.TotalCanonicalParts}");
            statsBuilder.AppendLine($"Learned Aliases:            {partStats.TotalAliasesLearned}");
            statsBuilder.AppendLine($"Bootstrap Parts:            {partStats.BootstrapPartsCount}");
            statsBuilder.AppendLine($"Abbreviations:              {partStats.AbbreviationsCount}");
            statsBuilder.AppendLine();

            // Top mined parts
            if (miningStats.TopParts.Any())
            {
                statsBuilder.AppendLine($"TOP LEARNED PARTS:");
                statsBuilder.AppendLine(new string('-', 40));
                foreach (var part in miningStats.TopParts.Take(10))
                {
                    statsBuilder.AppendLine($"  {part.Name,-30} ({part.TimesSeen}x, {part.AliasCount} aliases)");
                }
                statsBuilder.AppendLine();
            }

            if (stats.TopPatterns.Any())
            {
                statsBuilder.AppendLine($"TOP PATTERNS:");
                statsBuilder.AppendLine(new string('-', 40));
                foreach (var pattern in stats.TopPatterns)
                {
                    statsBuilder.AppendLine($"  - {pattern}");
                }
            }
            else
            {
                statsBuilder.AppendLine("No patterns learned yet. Import some estimates to get started!");
            }

            _statsText.Text = statsBuilder.ToString();

            // Refresh patterns list
            RefreshPatternsList();
        }

        private void RefreshPatternsList()
        {
            _patternsListView.Items.Clear();

            var patterns = EstimateLearningService.Instance.GetAllPatterns()
                .OrderByDescending(p => p.Confidence)
                .ThenByDescending(p => p.ExampleCount);

            foreach (var pattern in patterns)
            {
                var patternPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

                var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"{pattern.PartName} - {pattern.OperationType}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                });

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"Confidence: {pattern.Confidence:P0}",
                    Foreground = pattern.Confidence > 0.7
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 100))
                });

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"({pattern.ExampleCount} examples)",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
                });

                patternPanel.Children.Add(headerStack);

                // Show operations for this pattern
                foreach (var op in pattern.Operations.Take(3))
                {
                    patternPanel.Children.Add(new TextBlock
                    {
                        Text = $"    -> {op.Description} ({op.LaborHours:F1} hrs)",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                        FontSize = 12
                    });
                }

                if (pattern.Operations.Count > 3)
                {
                    patternPanel.Children.Add(new TextBlock
                    {
                        Text = $"    ... and {pattern.Operations.Count - 3} more operations",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120)),
                        FontSize = 12,
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    });
                }

                _patternsListView.Items.Add(patternPanel);
            }

            if (!patterns.Any())
            {
                _patternsListView.Items.Add(new TextBlock
                {
                    Text = "No patterns learned yet. Import estimates to build the database.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
        }
    }
}
