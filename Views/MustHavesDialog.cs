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

namespace McStudDesktop.Views
{
    /// <summary>
    /// Reusable Must-Haves configuration dialog.
    /// Features: preset templates, search, collapsible sections, autocomplete custom add, summary footer.
    /// </summary>
    public static class MustHavesDialog
    {
        // --- Color constants ---
        private static readonly Color BgDark = Color.FromArgb(255, 22, 27, 34);
        private static readonly Color BgMedium = Color.FromArgb(255, 28, 33, 40);
        private static readonly Color BgCard = Color.FromArgb(255, 35, 40, 48);
        private static readonly Color TextMuted = Color.FromArgb(255, 180, 185, 190);
        private static readonly Color TextDim = Color.FromArgb(255, 140, 140, 140);
        private static readonly Color GreenAccent = Color.FromArgb(255, 100, 200, 100);
        private static readonly Color GoldAccent = Color.FromArgb(255, 255, 200, 100);
        private static readonly Color CustomAccent = Color.FromArgb(255, 180, 140, 50);
        private static readonly Color DeleteBg = Color.FromArgb(255, 75, 40, 40);
        private static readonly Color DeleteFg = Color.FromArgb(255, 220, 120, 120);

        // Section accent colors
        private static readonly Color ElectricalColor = Color.FromArgb(255, 100, 180, 255);
        private static readonly Color DiagnosticsColor = Color.FromArgb(255, 80, 200, 220);
        private static readonly Color RefinishColor = Color.FromArgb(255, 255, 180, 100);
        private static readonly Color CoverCarColor = Color.FromArgb(255, 180, 220, 180);
        private static readonly Color BodyColor = Color.FromArgb(255, 220, 150, 150);
        private static readonly Color MechanicalColor = Color.FromArgb(255, 200, 180, 255);

        // Preset definitions: description keywords to match against SOP items
        private static readonly Dictionary<string, string[]> Presets = new()
        {
            ["Every Estimate"] = new[]
            {
                "pre-scan", "pre scan", "post-scan", "post scan", "in-process scan", "in process scan",
                "battery disconnect", "electronic reset", "clean for delivery", "parts disposal",
                "hazmat", "misc hardware", "miscellaneous hardware", "seat cover", "wheel cover",
                "seat/wheel cover", "oem research", "oem procedure", "dynamic systems", "test battery"
            },
            ["Refinish Job"] = new[]
            {
                "color tint", "spray out card", "cover car for overspray", "cover for edging",
                "mask for buffing", "cover engine", "cover interior for refinish",
                "cover interior for repair", "clean for primer", "cover for primer",
                "refinish material invoice"
            },
            ["Structural"] = new[]
            {
                "pre-scan", "pre scan", "post-scan", "post scan", "in-process scan",
                "battery disconnect", "battery reconnect", "cover electrical",
                "battery support", "mask removed", "glass cleaner", "oem research", "oem procedure"
            },
            ["ADAS Vehicle"] = new[]
            {
                "pre-scan", "pre scan", "post-scan", "post scan", "adas report", "adas",
                "simulate fluid", "tire pressure", "remove belongings", "customer belongings",
                "charge battery", "dynamic systems", "oem research", "oem procedure"
            }
        };

        /// <summary>
        /// Show the Must-Haves configuration dialog.
        /// Returns true if the user saved changes.
        /// </summary>
        public static async Task<bool> ShowAsync(XamlRoot xamlRoot)
        {
            var config = GhostConfigService.Instance;
            var excel = ExcelGhostDataProvider.Instance;
            var existingMustHaves = config.GetMustHaves();

            var dialogStack = new StackPanel { Spacing = 10, MinWidth = 640 };

            dialogStack.Children.Add(new TextBlock
            {
                Text = "Check the operations that should always appear on every estimate. Scoring will flag any that are missing.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                TextWrapping = TextWrapping.Wrap
            });

            // === PRESET BUTTONS ===
            var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var activePresets = new HashSet<string>();

            // Will be populated after checkboxes are built
            var allCheckBoxes = new Dictionary<CheckBox, (string Description, string Section, decimal Price, decimal Hours)>();
            var sectionPanels = new Dictionary<string, (StackPanel Panel, Border Header, TextBlock CountText, Button CheckAllBtn)>();

            // === SEARCH BOX ===
            var searchBox = new AutoSuggestBox
            {
                PlaceholderText = "Search operations...",
                QueryIcon = new SymbolIcon(Symbol.Find),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };

            // === SUMMARY FOOTER ===
            var summaryText = new TextBlock
            {
                Text = "0 items checked  |  $0 total  |  0.0 hrs labor",
                FontSize = 12,
                Foreground = new SolidColorBrush(GreenAccent),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };

            void UpdateSummary()
            {
                int count = 0;
                decimal totalPrice = 0;
                decimal totalHours = 0;
                foreach (var (cb, info) in allCheckBoxes)
                {
                    if (cb.IsChecked == true)
                    {
                        count++;
                        totalPrice += info.Price;
                        totalHours += info.Hours;
                    }
                }
                summaryText.Text = $"{count} items checked  |  ${totalPrice:N0} total  |  {totalHours:F1} hrs labor";
            }

            void UpdateSectionCount(string sectionLabel)
            {
                if (!sectionPanels.ContainsKey(sectionLabel)) return;
                var (panel, _, countText, _) = sectionPanels[sectionLabel];
                int total = 0, checkedCount = 0;
                foreach (var (cb, info) in allCheckBoxes)
                {
                    if (info.Section == sectionLabel)
                    {
                        total++;
                        if (cb.IsChecked == true) checkedCount++;
                    }
                }
                countText.Text = $"{total} items, {checkedCount} checked";
            }

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420
            };

            var sectionsStack = new StackPanel { Spacing = 8 };

            // Build SOP sections from Excel data
            var sopSections = new (string SheetName, string SectionLabel, Color AccentColor, Func<string, bool>? Filter)[]
            {
                ("SOP List", "Electrical", ElectricalColor,
                    desc => { var d = desc.ToLowerInvariant();
                        return d.Contains("battery") || d.Contains("disconnect") || d.Contains("reconnect") ||
                               d.Contains("electronic") || d.Contains("reset") || d.Contains("charge") ||
                               d.Contains("maintain") || (d.Contains("cover") && d.Contains("electrical")); }),
                ("SOP List", "Vehicle Diagnostics", DiagnosticsColor,
                    desc => { var d = desc.ToLowerInvariant();
                        return d.Contains("scan") || d.Contains("diagnostic") || d.Contains("oem") ||
                               d.Contains("dynamic") || d.Contains("adas") || d.Contains("simulate") ||
                               d.Contains("tire pressure") || d.Contains("fluids") || d.Contains("systems verification") ||
                               d.Contains("drive cycle") || d.Contains("customer belongings"); }),
                ("Refinish Operations", "Refinish Operations", RefinishColor, null),
                ("Cover Car Operations", "Cover Car Operations", CoverCarColor, null),
                ("Body Operations", "Body / Structural", BodyColor, null),
                ("Mechanical Operations", "Mechanical Operations", MechanicalColor, null),
            };

            // All known operation descriptions for autocomplete
            var allKnownOps = new List<(string Description, decimal Price, decimal Hours)>();

            foreach (var (sheetName, sectionLabel, accent, filter) in sopSections)
            {
                var ops = excel.GetSheetOperations(sheetName);
                var cleanOps = ops.Where(o =>
                    !string.IsNullOrWhiteSpace(o.Description) &&
                    !o.Description.Contains("\U0001f517") && !o.Description.Contains("\U0001f4ca") &&
                    !o.Description.Contains("\U0001f4b2") && !o.Description.Contains("\U0001f6e0") &&
                    !o.Description.Contains("\U0001f3a8") &&
                    !o.Description.StartsWith("Back to top", StringComparison.OrdinalIgnoreCase) &&
                    !o.Description.StartsWith("Category", StringComparison.OrdinalIgnoreCase) &&
                    !o.Description.Contains("\t") &&
                    o.Description.Length >= 4).ToList();

                if (filter != null)
                    cleanOps = cleanOps.Where(o => filter(o.Description!)).ToList();

                if (cleanOps.Count == 0) continue;

                // Collect for autocomplete
                foreach (var op in cleanOps)
                    allKnownOps.Add((op.Description!.Trim(), op.Price, op.LaborHours > 0 ? op.LaborHours : op.RefinishHours));

                // Section wrapper
                var sectionItemsPanel = new StackPanel { Spacing = 2 };

                // Collapsible section header
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Chevron
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Label
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Count
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Check All

                var chevron = new TextBlock
                {
                    Text = "\uE70D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var headerLabel = new TextBlock
                {
                    Text = sectionLabel,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var countLabel = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextDim),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                var checkAllBtn = new Button
                {
                    Content = "Check All",
                    FontSize = 10,
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
                    Padding = new Thickness(8, 2, 8, 2),
                    CornerRadius = new CornerRadius(3),
                    MinWidth = 0,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(chevron, 0);
                Grid.SetColumn(headerLabel, 1);
                Grid.SetColumn(countLabel, 2);
                Grid.SetColumn(checkAllBtn, 3);
                headerGrid.Children.Add(chevron);
                headerGrid.Children.Add(headerLabel);
                headerGrid.Children.Add(countLabel);
                headerGrid.Children.Add(checkAllBtn);

                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(BgCard),
                    Padding = new Thickness(10, 6, 10, 6),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Child = headerGrid
                };

                // Toggle collapse on header click
                var capturedSection = sectionLabel;
                headerBorder.Tapped += (s, ev) =>
                {
                    if (sectionPanels.ContainsKey(capturedSection))
                    {
                        var p = sectionPanels[capturedSection].Panel;
                        bool isVisible = p.Visibility == Visibility.Visible;
                        p.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                        chevron.Text = isVisible ? "\uE76C" : "\uE70D";
                    }
                };

                sectionPanels[sectionLabel] = (sectionItemsPanel, headerBorder, countLabel, checkAllBtn);
                sectionsStack.Children.Add(headerBorder);
                sectionsStack.Children.Add(sectionItemsPanel);

                // Check All button logic
                var capturedLabel = sectionLabel;
                checkAllBtn.Click += (s, ev) =>
                {
                    // Toggle: if all checked → uncheck all, else check all
                    var sectionCbs = allCheckBoxes.Where(kv => kv.Value.Section == capturedLabel).Select(kv => kv.Key).ToList();
                    bool allChecked = sectionCbs.All(cb => cb.IsChecked == true);
                    foreach (var cb in sectionCbs)
                        cb.IsChecked = !allChecked;
                    UpdateSectionCount(capturedLabel);
                    UpdateSummary();
                };

                foreach (var op in cleanOps)
                {
                    var desc = op.Description!.Trim();
                    var isChecked = existingMustHaves.Any(m =>
                        m.Enabled && (
                            m.Description.Equals(desc, StringComparison.OrdinalIgnoreCase) ||
                            m.Description.Replace("-", " ").Equals(desc.Replace("-", " "), StringComparison.OrdinalIgnoreCase) ||
                            desc.ToLowerInvariant().Contains(m.Description.ToLowerInvariant()) ||
                            m.Description.ToLowerInvariant().Contains(desc.ToLowerInvariant())
                        ));

                    var priceStr = op.Price > 0 ? $"${op.Price:N0}" : "";
                    var hoursStr = op.LaborHours > 0 ? $"{op.LaborHours:F1}h" :
                                    op.RefinishHours > 0 ? $"{op.RefinishHours:F1}h refn" : "";
                    var detail = string.Join("  ", new[] { hoursStr, priceStr }.Where(s => s.Length > 0));

                    // Look up condition from saved config (canonical conditions are on the MustHaveOperation)
                    var savedMh = existingMustHaves.FirstOrDefault(m =>
                        m.Description.Equals(desc, StringComparison.OrdinalIgnoreCase) ||
                        m.Description.Replace("-", " ").Equals(desc.Replace("-", " "), StringComparison.OrdinalIgnoreCase));
                    var condition = savedMh?.Conditions ?? "always";

                    var rowGrid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var cb = new CheckBox
                    {
                        Content = desc,
                        IsChecked = isChecked,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.White),
                        Tag = desc.ToLowerInvariant()  // for search filtering
                    };
                    cb.Checked += (s, ev) => { UpdateSectionCount(sectionLabel); UpdateSummary(); };
                    cb.Unchecked += (s, ev) => { UpdateSectionCount(sectionLabel); UpdateSummary(); };

                    Grid.SetColumn(cb, 0);
                    rowGrid.Children.Add(cb);

                    if (detail.Length > 0)
                    {
                        var detailText = new TextBlock
                        {
                            Text = detail,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(GreenAccent),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(8, 0, 0, 0)
                        };
                        Grid.SetColumn(detailText, 1);
                        rowGrid.Children.Add(detailText);
                    }

                    // Show condition tag for non-"always" items
                    if (!string.IsNullOrEmpty(condition) && condition != "always")
                    {
                        var condTag = new TextBlock
                        {
                            Text = $"[{condition}]",
                            FontSize = 9,
                            Foreground = new SolidColorBrush(TextDim),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 0, 0)
                        };
                        Grid.SetColumn(condTag, 2);
                        rowGrid.Children.Add(condTag);
                    }

                    sectionItemsPanel.Children.Add(rowGrid);
                    allCheckBoxes[cb] = (desc, sectionLabel, op.Price, op.LaborHours > 0 ? op.LaborHours : op.RefinishHours);
                }
            }

            // === CUSTOM ADD SECTION ===
            var customSectionPanel = new StackPanel { Spacing = 2 };
            var customChevron = new TextBlock
            {
                Text = "\uE70D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(CustomAccent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var customHeaderBorder = new Border
            {
                Background = new SolidColorBrush(BgCard),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 4, 0, 0),
                BorderBrush = new SolidColorBrush(CustomAccent),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        customChevron,
                        new TextBlock
                        {
                            Text = "Custom Must-Have",
                            FontSize = 13,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(CustomAccent)
                        }
                    }
                }
            };
            customHeaderBorder.Tapped += (s, ev) =>
            {
                bool isVisible = customSectionPanel.Visibility == Visibility.Visible;
                customSectionPanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                customChevron.Text = isVisible ? "\uE76C" : "\uE70D";
            };

            // AutoSuggestBox for custom add (with autocomplete from all known ops)
            var nameBox = new AutoSuggestBox
            {
                PlaceholderText = "Type operation name...",
                Width = 240,
                FontSize = 12
            };
            var priceBox = new NumberBox { PlaceholderText = "$", Minimum = 0, Maximum = 5000, Width = 80, FontSize = 12, Header = "Price" };
            var hoursBox = new NumberBox { PlaceholderText = "hrs", Minimum = 0, Maximum = 50, SmallChange = 0.1, Width = 80, FontSize = 12, Header = "Hours" };
            var conditionBox = new ComboBox
            {
                IsEditable = true,
                PlaceholderText = "Condition",
                Width = 150,
                FontSize = 12,
                SelectedIndex = 0
            };
            foreach (var cond in EstimateConditionEvaluator.AllConditions)
                conditionBox.Items.Add(cond);

            var addBtn = new Button
            {
                Content = "Add",
                Background = new SolidColorBrush(Color.FromArgb(255, 120, 90, 40)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Bottom
            };

            // AutoSuggest: filter known ops by text
            nameBox.TextChanged += (s, args) =>
            {
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var query = nameBox.Text?.ToLowerInvariant() ?? "";
                    if (query.Length >= 2)
                    {
                        var suggestions = allKnownOps
                            .Where(o => o.Description.ToLowerInvariant().Contains(query))
                            .Select(o => o.Description)
                            .Distinct()
                            .Take(10)
                            .ToList();
                        nameBox.ItemsSource = suggestions;
                    }
                    else
                    {
                        nameBox.ItemsSource = null;
                    }
                }
            };

            nameBox.SuggestionChosen += (s, args) =>
            {
                var chosen = args.SelectedItem?.ToString() ?? "";
                var match = allKnownOps.FirstOrDefault(o =>
                    o.Description.Equals(chosen, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Description))
                {
                    if (match.Price > 0) priceBox.Value = (double)match.Price;
                    if (match.Hours > 0) hoursBox.Value = (double)match.Hours;
                }
            };

            var customAddsPanel = new StackPanel { Spacing = 2, Margin = new Thickness(8, 4, 0, 0) };

            // Show existing custom must-haves (ones not from SOP)
            foreach (var mh in existingMustHaves.Where(m => !allCheckBoxes.Values.Any(v =>
                v.Description.Equals(m.Description, StringComparison.OrdinalIgnoreCase))))
            {
                AddCustomRow(mh, config, allCheckBoxes, customAddsPanel, UpdateSummary);
            }

            addBtn.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
                var price = !double.IsNaN(priceBox.Value) ? (decimal)priceBox.Value : 0;
                var hours = !double.IsNaN(hoursBox.Value) ? (decimal)hoursBox.Value : 0;
                var selectedCondition = conditionBox.SelectedItem?.ToString() ?? conditionBox.Text ?? "always";
                if (string.IsNullOrWhiteSpace(selectedCondition)) selectedCondition = "always";
                var newMh = new MustHaveOperation
                {
                    Description = nameBox.Text.Trim(),
                    Section = "Custom",
                    ExpectedPrice = price,
                    ExpectedHours = hours,
                    Conditions = selectedCondition
                };
                config.AddMustHave(newMh);

                var newCb = new CheckBox
                {
                    Content = newMh.Description,
                    IsChecked = true,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    Tag = newMh.Description.ToLowerInvariant()
                };
                newCb.Checked += (s2, ev2) => UpdateSummary();
                newCb.Unchecked += (s2, ev2) => UpdateSummary();
                allCheckBoxes[newCb] = (newMh.Description, "Custom", price, hours);
                customAddsPanel.Children.Add(newCb);
                nameBox.Text = "";
                priceBox.Value = double.NaN;
                hoursBox.Value = double.NaN;
                conditionBox.SelectedIndex = 0;
                UpdateSummary();
            };

            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 4, 0, 0) };
            addRow.Children.Add(nameBox);
            addRow.Children.Add(priceBox);
            addRow.Children.Add(hoursBox);
            addRow.Children.Add(conditionBox);
            addRow.Children.Add(addBtn);

            customSectionPanel.Children.Add(addRow);
            customSectionPanel.Children.Add(customAddsPanel);

            sectionsStack.Children.Add(customHeaderBorder);
            sectionsStack.Children.Add(customSectionPanel);

            // === SEARCH LOGIC ===
            searchBox.TextChanged += (s, args) =>
            {
                var query = searchBox.Text?.ToLowerInvariant() ?? "";

                foreach (var (sectionLabel, (panel, header, countText, checkAll)) in sectionPanels)
                {
                    int visibleCount = 0;
                    foreach (var child in panel.Children)
                    {
                        if (child is Grid rowGrid)
                        {
                            bool matches = string.IsNullOrEmpty(query);
                            if (!matches)
                            {
                                // Search checkbox content
                                foreach (var element in rowGrid.Children)
                                {
                                    if (element is CheckBox cb && cb.Tag is string tag)
                                    {
                                        matches = tag.Contains(query);
                                        break;
                                    }
                                }
                            }
                            rowGrid.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                            if (matches) visibleCount++;
                        }
                    }
                    // Hide section header if no visible children
                    header.Visibility = (string.IsNullOrEmpty(query) || visibleCount > 0) ? Visibility.Visible : Visibility.Collapsed;
                    panel.Visibility = (string.IsNullOrEmpty(query) || visibleCount > 0) ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // === PRESET BUTTONS ===
            foreach (var (presetName, keywords) in Presets)
            {
                var capturedName = presetName;
                var capturedKeywords = keywords;
                var presetBtn = new Button
                {
                    Content = presetName,
                    FontSize = 11,
                    Background = new SolidColorBrush(BgCard),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 210, 220)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 65, 75)),
                    Padding = new Thickness(12, 4, 12, 4),
                    CornerRadius = new CornerRadius(12)
                };

                presetBtn.Click += (s, ev) =>
                {
                    bool isActive = activePresets.Contains(capturedName);

                    // Find matching checkboxes
                    var matching = allCheckBoxes.Where(kv =>
                    {
                        var descLower = kv.Value.Description.ToLowerInvariant();
                        return capturedKeywords.Any(kw => descLower.Contains(kw));
                    }).Select(kv => kv.Key).ToList();

                    if (isActive)
                    {
                        // Uncheck matching items
                        foreach (var cb in matching) cb.IsChecked = false;
                        activePresets.Remove(capturedName);
                        presetBtn.Background = new SolidColorBrush(BgCard);
                        presetBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 65, 75));
                    }
                    else
                    {
                        // Check matching items
                        foreach (var cb in matching) cb.IsChecked = true;
                        activePresets.Add(capturedName);
                        presetBtn.Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 80));
                        presetBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 140, 200));
                    }

                    // Update all section counts
                    foreach (var sec in sectionPanels.Keys)
                        UpdateSectionCount(sec);
                    UpdateSummary();
                };

                presetRow.Children.Add(presetBtn);
            }

            // === ASSEMBLE DIALOG ===
            dialogStack.Children.Add(presetRow);
            dialogStack.Children.Add(searchBox);
            scrollViewer.Content = sectionsStack;
            dialogStack.Children.Add(scrollViewer);
            dialogStack.Children.Add(summaryText);

            // Initialize section counts and summary
            foreach (var sec in sectionPanels.Keys)
                UpdateSectionCount(sec);
            UpdateSummary();

            var dialog = new ContentDialog
            {
                Title = "Must-Haves \u2014 Required on Every Estimate",
                Content = dialogStack,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SaveMustHaves(config, allCheckBoxes);
                return true;
            }
            return false;
        }

        private static void AddCustomRow(
            MustHaveOperation mh,
            GhostConfigService config,
            Dictionary<CheckBox, (string Description, string Section, decimal Price, decimal Hours)> checkBoxMap,
            StackPanel customAddsPanel,
            Action updateSummary)
        {
            var customRow = new Grid();
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0: checkbox
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: info
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: condition tag
            customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: delete

            var cb = new CheckBox
            {
                Content = mh.Description,
                IsChecked = mh.Enabled,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                Tag = mh.Description.ToLowerInvariant()
            };
            cb.Checked += (s, ev) => updateSummary();
            cb.Unchecked += (s, ev) => updateSummary();
            Grid.SetColumn(cb, 0);
            customRow.Children.Add(cb);

            var infoText = "";
            if (mh.ExpectedPrice > 0) infoText += $"${mh.ExpectedPrice:N0}  ";
            if (mh.ExpectedHours > 0) infoText += $"{mh.ExpectedHours:F1}h";
            if (infoText.Length > 0)
            {
                var info = new TextBlock
                {
                    Text = infoText.Trim(),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(GreenAccent),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(info, 1);
                customRow.Children.Add(info);
            }

            // Show condition tag for non-"always" custom items
            if (!string.IsNullOrEmpty(mh.Conditions) && mh.Conditions != "always")
            {
                var condTag = new TextBlock
                {
                    Text = $"[{mh.Conditions}]",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextDim),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(condTag, 2);
                customRow.Children.Add(condTag);
            }

            var capturedMh = mh;
            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 },
                Padding = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(DeleteBg),
                Foreground = new SolidColorBrush(DeleteFg),
                CornerRadius = new CornerRadius(3),
                MinWidth = 0,
                Margin = new Thickness(8, 0, 0, 0)
            };
            deleteBtn.Click += (s, ev) =>
            {
                config.RemoveMustHave(capturedMh.Id);
                customRow.Visibility = Visibility.Collapsed;
                updateSummary();
            };
            Grid.SetColumn(deleteBtn, 3);
            customRow.Children.Add(deleteBtn);

            checkBoxMap[cb] = (mh.Description, "Custom", mh.ExpectedPrice, mh.ExpectedHours);
            customAddsPanel.Children.Add(customRow);
        }

        private static void SaveMustHaves(
            GhostConfigService config,
            Dictionary<CheckBox, (string Description, string Section, decimal Price, decimal Hours)> checkBoxMap)
        {
            // Sync: remove all SOP-based must-haves, re-add checked ones
            var currentMustHaves = config.GetMustHaves();

            var sopDescriptions = checkBoxMap.Values
                .Where(v => v.Section != "Custom")
                .Select(v => v.Description).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var mh in currentMustHaves.Where(m => sopDescriptions.Contains(m.Description)).ToList())
            {
                config.RemoveMustHave(mh.Id);
            }

            // Add checked SOP items
            foreach (var (cb, info) in checkBoxMap)
            {
                if (cb.IsChecked == true && info.Section != "Custom")
                {
                    if (!config.GetMustHaves().Any(m => m.Description.Equals(info.Description, StringComparison.OrdinalIgnoreCase)))
                    {
                        config.AddMustHave(new MustHaveOperation
                        {
                            Description = info.Description,
                            Section = info.Section,
                            ExpectedPrice = info.Price,
                            ExpectedHours = info.Hours
                        });
                    }
                }
            }

            // Handle unchecked custom items (disable them)
            foreach (var (cb, info) in checkBoxMap)
            {
                if (info.Section == "Custom")
                {
                    var existing = config.GetMustHaves().FirstOrDefault(m =>
                        m.Description.Equals(info.Description, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Enabled = cb.IsChecked == true;
                        config.UpdateMustHave(existing);
                    }
                }
            }
        }
    }
}
