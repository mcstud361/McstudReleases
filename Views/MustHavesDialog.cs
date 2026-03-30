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

            // Track inline edits: CheckBox → (Description, Hours, Price, Condition)
            // Populated when user clicks pencil and confirms via Done button
            var pendingEdits = new Dictionary<CheckBox, (string Description, decimal Hours, decimal Price, string Condition)>();

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
                MaxHeight = 560
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

                // Collect for autocomplete (exclude invalid entries like *Description)
                foreach (var op in cleanOps)
                {
                    var trimmedDesc = op.Description!.Trim();
                    if (!trimmedDesc.StartsWith("*") && trimmedDesc.Any(char.IsLetter))
                        allKnownOps.Add((trimmedDesc, op.Price, op.LaborHours > 0 ? op.LaborHours : op.RefinishHours));
                }

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

                    var opPrice = op.Price;
                    var opHours = op.LaborHours > 0 ? op.LaborHours : op.RefinishHours;

                    // Look up saved values (user may have edited hours/price previously)
                    var savedMh = existingMustHaves.FirstOrDefault(m =>
                        m.Description.Equals(desc, StringComparison.OrdinalIgnoreCase) ||
                        m.Description.Replace("-", " ").Equals(desc.Replace("-", " "), StringComparison.OrdinalIgnoreCase));
                    var condition = savedMh?.Conditions ?? "always";
                    // Use saved values if they differ from SOP defaults (user edited them)
                    if (savedMh != null)
                    {
                        if (savedMh.ExpectedPrice != opPrice && savedMh.ExpectedPrice > 0)
                            opPrice = savedMh.ExpectedPrice;
                        if (savedMh.ExpectedHours != opHours && savedMh.ExpectedHours > 0)
                            opHours = savedMh.ExpectedHours;
                    }

                    var priceStr = opPrice > 0 ? $"${opPrice:N0}" : "";
                    var hoursStr = opHours > 0 ? $"{opHours:F1}h" : "";
                    var detail = string.Join("  ", new[] { hoursStr, priceStr }.Where(s => s.Length > 0));

                    // Wrapper StackPanel to hold both the row and the edit panel
                    var rowWrapper = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0: checkbox
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: detail
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: condition
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: pencil

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

                    var detailText = new TextBlock
                    {
                        Text = detail,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(GreenAccent),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                        Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed
                    };
                    Grid.SetColumn(detailText, 1);
                    rowGrid.Children.Add(detailText);

                    var condTag = new TextBlock
                    {
                        Text = (!string.IsNullOrEmpty(condition) && condition != "always") ? $"[{condition}]" : "",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(TextDim),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0),
                        Visibility = (!string.IsNullOrEmpty(condition) && condition != "always") ? Visibility.Visible : Visibility.Collapsed
                    };
                    Grid.SetColumn(condTag, 2);
                    rowGrid.Children.Add(condTag);

                    // Pencil (edit) button
                    var pencilBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE70F", FontSize = 10 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(TextDim),
                        CornerRadius = new CornerRadius(3),
                        MinWidth = 0,
                        Margin = new Thickness(4, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(pencilBtn, 3);
                    rowGrid.Children.Add(pencilBtn);

                    // Inline edit panel (hidden by default)
                    var editPanel = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                        Padding = new Thickness(10, 6, 10, 6),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(20, 2, 0, 4),
                        Visibility = Visibility.Collapsed
                    };

                    var editGrid = new Grid();
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Hours
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Price
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Condition
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Spacer
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Delete
                    editGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Done

                    var editHoursBox = new NumberBox
                    {
                        Header = "Hours", Minimum = 0, Maximum = 50, SmallChange = 0.1,
                        Value = opHours > 0 ? (double)opHours : double.NaN,
                        Width = 80, FontSize = 11, Margin = new Thickness(0, 0, 6, 0)
                    };
                    var editPriceBox = new NumberBox
                    {
                        Header = "Price $", Minimum = 0, Maximum = 5000,
                        Value = opPrice > 0 ? (double)opPrice : double.NaN,
                        Width = 80, FontSize = 11, Margin = new Thickness(0, 0, 6, 0)
                    };
                    var editCondBox = new ComboBox
                    {
                        Header = "Condition", IsEditable = true, Width = 130, FontSize = 11,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    foreach (var cond in EstimateConditionEvaluator.AllConditions)
                        editCondBox.Items.Add(cond);
                    editCondBox.SelectedItem = condition;

                    var editDeleteBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 },
                        Padding = new Thickness(6, 4, 6, 4),
                        Background = new SolidColorBrush(DeleteBg),
                        Foreground = new SolidColorBrush(DeleteFg),
                        CornerRadius = new CornerRadius(3),
                        MinWidth = 0,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    var editDoneBtn = new Button
                    {
                        Content = "Done",
                        Padding = new Thickness(10, 4, 10, 4),
                        Background = new SolidColorBrush(Color.FromArgb(255, 40, 80, 60)),
                        Foreground = new SolidColorBrush(GreenAccent),
                        CornerRadius = new CornerRadius(3),
                        MinWidth = 0,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };

                    Grid.SetColumn(editHoursBox, 0);
                    Grid.SetColumn(editPriceBox, 1);
                    Grid.SetColumn(editCondBox, 2);
                    Grid.SetColumn(editDeleteBtn, 4);
                    Grid.SetColumn(editDoneBtn, 5);
                    editGrid.Children.Add(editHoursBox);
                    editGrid.Children.Add(editPriceBox);
                    editGrid.Children.Add(editCondBox);
                    editGrid.Children.Add(editDeleteBtn);
                    editGrid.Children.Add(editDoneBtn);
                    editPanel.Child = editGrid;

                    // Capture for closures
                    var capturedCb = cb;
                    var capturedDetailText = detailText;
                    var capturedCondTag = condTag;
                    var capturedEditPanel = editPanel;
                    var capturedSectionLabel = sectionLabel;
                    var capturedRowWrapper = rowWrapper;

                    pencilBtn.Click += (s, ev) =>
                    {
                        // Toggle edit panel visibility
                        capturedEditPanel.Visibility = capturedEditPanel.Visibility == Visibility.Visible
                            ? Visibility.Collapsed : Visibility.Visible;
                    };

                    editDoneBtn.Click += (s, ev) =>
                    {
                        var newHours = !double.IsNaN(editHoursBox.Value) ? (decimal)editHoursBox.Value : 0;
                        var newPrice = !double.IsNaN(editPriceBox.Value) ? (decimal)editPriceBox.Value : 0;
                        var newCond = editCondBox.SelectedItem?.ToString() ?? editCondBox.Text ?? "always";
                        if (string.IsNullOrWhiteSpace(newCond)) newCond = "always";

                        // Update the display
                        var newHoursStr = newHours > 0 ? $"{newHours:F1}h" : "";
                        var newPriceStr = newPrice > 0 ? $"${newPrice:N0}" : "";
                        var newDetail = string.Join("  ", new[] { newHoursStr, newPriceStr }.Where(str => str.Length > 0));
                        capturedDetailText.Text = newDetail;
                        capturedDetailText.Visibility = newDetail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                        if (!string.IsNullOrEmpty(newCond) && newCond != "always")
                        {
                            capturedCondTag.Text = $"[{newCond}]";
                            capturedCondTag.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            capturedCondTag.Text = "";
                            capturedCondTag.Visibility = Visibility.Collapsed;
                        }

                        // Update the checkbox map with new values
                        var currentInfo = allCheckBoxes[capturedCb];
                        allCheckBoxes[capturedCb] = (currentInfo.Description, currentInfo.Section, newPrice, newHours);

                        // Track this edit for save
                        pendingEdits[capturedCb] = (currentInfo.Description, newHours, newPrice, newCond);

                        capturedEditPanel.Visibility = Visibility.Collapsed;
                        UpdateSummary();
                    };

                    editDeleteBtn.Click += (s, ev) =>
                    {
                        capturedCb.IsChecked = false;
                        capturedRowWrapper.Visibility = Visibility.Collapsed;
                        allCheckBoxes.Remove(capturedCb);
                        pendingEdits.Remove(capturedCb);
                        UpdateSectionCount(capturedSectionLabel);
                        UpdateSummary();
                    };

                    rowWrapper.Children.Add(rowGrid);
                    rowWrapper.Children.Add(editPanel);
                    sectionItemsPanel.Children.Add(rowWrapper);
                    allCheckBoxes[cb] = (desc, sectionLabel, opPrice, opHours);
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

            // AutoSuggest: filter known ops by text (exclude invalid entries)
            nameBox.TextChanged += (s, args) =>
            {
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var query = nameBox.Text?.ToLowerInvariant() ?? "";
                    if (query.Length >= 2)
                    {
                        var suggestions = allKnownOps
                            .Where(o => o.Description.ToLowerInvariant().Contains(query) &&
                                        !o.Description.StartsWith("*") &&
                                        o.Description.Any(char.IsLetter))
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
                var trimmedName = nameBox.Text.Trim();
                // Reject invalid entries: starts with *, or contains no letters
                if (trimmedName.StartsWith("*") || !trimmedName.Any(char.IsLetter)) return;
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
                        // Rows are wrapped in StackPanels (containing Grid + edit panel)
                        var rowContainer = child;
                        Grid? rowGrid = null;
                        if (child is StackPanel wrapper && wrapper.Children.Count > 0 && wrapper.Children[0] is Grid g)
                            rowGrid = g;
                        else if (child is Grid directGrid)
                            rowGrid = directGrid;

                        if (rowGrid != null)
                        {
                            bool matches = string.IsNullOrEmpty(query);
                            if (!matches)
                            {
                                foreach (var element in rowGrid.Children)
                                {
                                    if (element is CheckBox cb && cb.Tag is string tag)
                                    {
                                        matches = tag.Contains(query);
                                        break;
                                    }
                                }
                            }
                            rowContainer.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                            if (matches) visibleCount++;
                        }
                    }
                    // Hide section header if no visible children
                    header.Visibility = (string.IsNullOrEmpty(query) || visibleCount > 0) ? Visibility.Visible : Visibility.Collapsed;
                    panel.Visibility = (string.IsNullOrEmpty(query) || visibleCount > 0) ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // === BUILT-IN PRESET BUTTONS ===
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

            // === USER TEMPLATE BUTTONS ===
            var userTemplateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            void RebuildUserTemplateButtons()
            {
                userTemplateRow.Children.Clear();
                var templates = GhostConfigService.Instance.GetMustHaveTemplates();
                foreach (var tmpl in templates)
                {
                    var capturedTmpl = tmpl;
                    var capturedDescs = tmpl.Descriptions;

                    // Button content: name + small delete X
                    var btnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    btnContent.Children.Add(new TextBlock
                    {
                        Text = tmpl.Name,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var deleteX = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE711", FontSize = 8 },
                        Padding = new Thickness(2),
                        MinWidth = 0, MinHeight = 0,
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(DeleteFg),
                        VerticalAlignment = VerticalAlignment.Center,
                        CornerRadius = new CornerRadius(8)
                    };

                    var tmplBtn = new Button
                    {
                        Content = btnContent,
                        FontSize = 11,
                        Background = new SolidColorBrush(Color.FromArgb(255, 45, 40, 55)),
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 210, 190, 240)),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 65, 100)),
                        Padding = new Thickness(10, 4, 4, 4),
                        CornerRadius = new CornerRadius(12)
                    };

                    tmplBtn.Click += (s, ev) =>
                    {
                        bool isActive = activePresets.Contains(capturedTmpl.Id);
                        var matching = allCheckBoxes.Where(kv =>
                            capturedDescs.Any(d => kv.Value.Description.Equals(d, StringComparison.OrdinalIgnoreCase)))
                            .Select(kv => kv.Key).ToList();

                        if (isActive)
                        {
                            foreach (var cb in matching) cb.IsChecked = false;
                            activePresets.Remove(capturedTmpl.Id);
                            tmplBtn.Background = new SolidColorBrush(Color.FromArgb(255, 45, 40, 55));
                            tmplBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 65, 100));
                        }
                        else
                        {
                            foreach (var cb in matching) cb.IsChecked = true;
                            activePresets.Add(capturedTmpl.Id);
                            tmplBtn.Background = new SolidColorBrush(Color.FromArgb(255, 60, 45, 85));
                            tmplBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 140, 100, 200));
                        }

                        foreach (var sec in sectionPanels.Keys)
                            UpdateSectionCount(sec);
                        UpdateSummary();
                    };

                    deleteX.Click += async (s, ev) =>
                    {
                        var confirmDialog = new ContentDialog
                        {
                            Title = "Delete Template",
                            Content = $"Delete \"{capturedTmpl.Name}\"?",
                            PrimaryButtonText = "Delete",
                            CloseButtonText = "Cancel",
                            XamlRoot = xamlRoot,
                            RequestedTheme = ElementTheme.Dark
                        };
                        if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            GhostConfigService.Instance.DeleteMustHaveTemplate(capturedTmpl.Id);
                            activePresets.Remove(capturedTmpl.Id);
                            RebuildUserTemplateButtons();
                        }
                    };

                    btnContent.Children.Add(deleteX);
                    userTemplateRow.Children.Add(tmplBtn);
                }

                // "+ Save Template" button
                var saveTemplateBtn = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 4,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE710", FontSize = 10 },
                            new TextBlock { Text = "Save Template", FontSize = 11 }
                        }
                    },
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromArgb(255, 35, 55, 45)),
                    Foreground = new SolidColorBrush(GreenAccent),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 100, 200, 100)),
                    Padding = new Thickness(10, 4, 10, 4),
                    CornerRadius = new CornerRadius(12)
                };

                saveTemplateBtn.Click += async (s, ev) =>
                {
                    // Collect currently checked descriptions
                    var checkedDescs = allCheckBoxes
                        .Where(kv => kv.Key.IsChecked == true)
                        .Select(kv => kv.Value.Description)
                        .ToList();

                    if (checkedDescs.Count == 0) return;

                    var nameInput = new TextBox
                    {
                        PlaceholderText = "Template name...",
                        FontSize = 13,
                        Width = 300
                    };

                    var nameDialog = new ContentDialog
                    {
                        Title = "Save Template",
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = $"Save {checkedDescs.Count} checked operations as a template:",
                                    FontSize = 12,
                                    Foreground = new SolidColorBrush(TextMuted),
                                    TextWrapping = TextWrapping.Wrap
                                },
                                nameInput
                            }
                        },
                        PrimaryButtonText = "Save",
                        CloseButtonText = "Cancel",
                        XamlRoot = xamlRoot,
                        RequestedTheme = ElementTheme.Dark
                    };

                    if (await nameDialog.ShowAsync() == ContentDialogResult.Primary &&
                        !string.IsNullOrWhiteSpace(nameInput.Text))
                    {
                        GhostConfigService.Instance.SaveMustHaveTemplate(new MustHaveTemplate
                        {
                            Name = nameInput.Text.Trim(),
                            Descriptions = checkedDescs
                        });
                        RebuildUserTemplateButtons();
                    }
                };

                userTemplateRow.Children.Add(saveTemplateBtn);
            }

            RebuildUserTemplateButtons();

            // === ASSEMBLE DIALOG ===
            dialogStack.Children.Add(presetRow);
            dialogStack.Children.Add(userTemplateRow);
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
                SaveMustHaves(config, allCheckBoxes, pendingEdits);
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
            Dictionary<CheckBox, (string Description, string Section, decimal Price, decimal Hours)> checkBoxMap,
            Dictionary<CheckBox, (string Description, decimal Hours, decimal Price, string Condition)> pendingEdits)
        {
            // Sync: remove all SOP-based must-haves using fuzzy matching, then re-add checked ones.
            var currentMustHaves = config.GetMustHaves();

            var sopNormalizedDescriptions = checkBoxMap.Values
                .Where(v => v.Section != "Custom")
                .Select(v => GhostConfigService.NormalizeMustHaveDesc(v.Description))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Remove any existing must-have whose normalized description matches a SOP checkbox
            foreach (var mh in currentMustHaves
                .Where(m => m.Section != "Custom" &&
                    sopNormalizedDescriptions.Contains(GhostConfigService.NormalizeMustHaveDesc(m.Description)))
                .ToList())
            {
                config.RemoveMustHave(mh.Id);
            }

            // Add checked SOP items (AddMustHave has built-in dedup)
            foreach (var (cb, info) in checkBoxMap)
            {
                if (cb.IsChecked == true && info.Section != "Custom")
                {
                    var newMh = new MustHaveOperation
                    {
                        Description = info.Description,
                        Section = info.Section,
                        ExpectedPrice = info.Price,
                        ExpectedHours = info.Hours
                    };

                    // Apply pending edits if user modified this item
                    if (pendingEdits.TryGetValue(cb, out var edit))
                    {
                        newMh.ExpectedHours = edit.Hours;
                        newMh.ExpectedPrice = edit.Price;
                        newMh.Conditions = edit.Condition;
                    }

                    config.AddMustHave(newMh);
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
                        // Apply pending edits for custom items too
                        if (pendingEdits.TryGetValue(cb, out var edit))
                        {
                            existing.ExpectedHours = edit.Hours;
                            existing.ExpectedPrice = edit.Price;
                            existing.Conditions = edit.Condition;
                        }
                        config.UpdateMustHave(existing);
                    }
                }
            }
        }
    }
}
