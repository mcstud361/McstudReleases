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
    /// Features: preset templates, search, dynamic group-based sections, per-section add,
    /// inline editing with group reassignment, group management panel, summary footer.
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

        // CCC Operation Types (short, long)
        private static readonly (string Short, string Long)[] CccOpTypes = new[]
        {
            ("Repl", "Replace"),
            ("Rpr", "Repair"),
            ("Refn", "Refinish"),
            ("R&I", "Remove and Install"),
            ("Sect", "Section"),
            ("Algn", "Align"),
            ("Subl", "Sublet"),
            ("Blnd", "Blend")
        };

        // Body Labor Categories
        private static readonly string[] BodyLaborCategories = new[]
        {
            "Body", "Mechanical", "Frame", "Structural", "Diagnostic",
            "Electrical", "Glass", "PDR",
            "User Defined 1", "User Defined 2", "User Defined 3", "User Defined 4"
        };

        // Preset color swatches for custom group creation
        private static readonly string[] GroupColorSwatches = new[]
        {
            "#FF6B6B", "#FF9F43", "#FECA57", "#48DBFB", "#0ABDE3",
            "#10AC84", "#EE5A24", "#A55EEA", "#FD79A8", "#BADC58"
        };

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
        /// Parse hex color string to WinUI Color.
        /// </summary>
        private static Color ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            return Color.FromArgb(255, 100, 180, 255); // fallback blue
        }

        /// <summary>
        /// Show the Must-Haves configuration dialog.
        /// Returns true if the user saved changes.
        /// </summary>
        public static async Task<bool> ShowAsync(XamlRoot xamlRoot)
        {
            var config = GhostConfigService.Instance;
            var excel = ExcelGhostDataProvider.Instance;
            var mustHaves = config.GetMustHaves();
            var groups = config.GetMustHaveGroups();

            var dialogStack = new Grid { MinWidth = 960, MaxHeight = 700, RowSpacing = 10 };
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 0: description
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 1: presets
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 2: user templates
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 3: group mgmt
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 4: search
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 5: context filter
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // row 6: content (fills remaining)
            dialogStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 7: summary
            int _dialogRow = 0;

            var descText = new TextBlock
            {
                Text = "Check the operations that should always appear on every estimate. Scoring will flag any that are missing.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(descText, _dialogRow++);
            dialogStack.Children.Add(descText);

            // === TRACKING STATE ===
            var activePresets = new HashSet<string>();
            var allCheckBoxes = new Dictionary<CheckBox, (string Id, string Description, string Section, string? GroupId, decimal Price, decimal Hours)>();
            var sectionPanels = new Dictionary<string, (StackPanel Panel, Border Header, TextBlock CountText, Button CheckAllBtn, Border? AddFormBorder)>();
            var pendingEdits = new Dictionary<CheckBox, (string Description, string CccOpType, int Quantity, decimal Price, decimal BodyHours, string BodyLaborCategory, decimal RefinishHours, string Condition, string? GroupId)>();
            var pendingDeletes = new HashSet<string>(); // operation IDs to delete on save
            var pendingAdds = new List<MustHaveOperation>(); // new ops to add on save
            var pendingTagEdits = new Dictionary<string, (List<string> InsuranceCompanies, List<string> VehicleTypes)>(); // op ID -> tags

            // === PRESET BUTTONS ===
            var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };

            // === SEARCH BOX + FILTER ===
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var searchFilterBox = new ComboBox
            {
                FontSize = 11, Width = 140, SelectedIndex = 0,
                Items = { "All Fields", "Name", "Op Type", "Labor Category", "Condition", "Group" }
            };
            var searchBox = new AutoSuggestBox
            {
                PlaceholderText = "Search operations...",
                QueryIcon = new SymbolIcon(Symbol.Find),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            searchRow.Children.Add(searchFilterBox);
            searchRow.Children.Add(searchBox);

            // === CONTEXT FILTER BAR (Insurance Company / Vehicle Type) ===
            var contextFilterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var contextCategoryBox = new ComboBox
            {
                FontSize = 11, Width = 180, SelectedIndex = 0,
                Items = { "All Must-Haves", "Insurance Company", "Vehicle Type" }
            };
            var contextValueBox = new ComboBox
            {
                FontSize = 11, Width = 200, Visibility = Visibility.Collapsed, PlaceholderText = "Select..."
            };
            var contextInfoText = new TextBlock
            {
                Text = "Showing all must-haves (universal + tagged)",
                FontSize = 10, Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            string? activeContextCategory = null; // "insurance" or "vehicle"
            string? activeContextValue = null;     // e.g., "GEICO" or "Electric Vehicle"

            void PopulateContextValues()
            {
                contextValueBox.Items.Clear();
                if (contextCategoryBox.SelectedIndex == 1) // Insurance Company
                {
                    activeContextCategory = "insurance";
                    var insurers = EstimateHistoryDatabase.Instance.KnownInsurers;
                    // Also collect insurers already tagged on must-haves
                    var taggedInsurers = mustHaves.SelectMany(m => m.InsuranceCompanies)
                        .Concat(pendingAdds.SelectMany(m => m.InsuranceCompanies))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    var allInsurers = insurers.Concat(taggedInsurers)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s).ToList();
                    foreach (var ins in allInsurers)
                        contextValueBox.Items.Add(ins);
                    contextValueBox.Visibility = Visibility.Visible;
                }
                else if (contextCategoryBox.SelectedIndex == 2) // Vehicle Type
                {
                    activeContextCategory = "vehicle";
                    foreach (var vt in GhostConfigService.KnownVehicleFuelTypes)
                        contextValueBox.Items.Add(vt);
                    contextValueBox.Visibility = Visibility.Visible;
                }
                else
                {
                    activeContextCategory = null;
                    activeContextValue = null;
                    contextValueBox.Visibility = Visibility.Collapsed;
                    contextInfoText.Text = "Showing all must-haves (universal + tagged)";
                }
            }

            void ApplyContextFilter()
            {
                if (activeContextCategory == null || activeContextValue == null)
                {
                    // Show all rows — remove context-based visibility override
                    foreach (var (groupId, (panel, header, countText, checkAll, addForm)) in sectionPanels)
                    {
                        foreach (var child in panel.Children)
                        {
                            if (child is FrameworkElement fe && fe.Tag is Dictionary<string, string>)
                                fe.Visibility = Visibility.Visible;
                        }
                    }
                    contextInfoText.Text = "Showing all must-haves (universal + tagged)";
                    return;
                }

                int shownCount = 0;
                foreach (var (groupId, (panel, header, countText, checkAll, addForm)) in sectionPanels)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is FrameworkElement fe && fe.Tag is Dictionary<string, string> tags)
                        {
                            // Check if this must-have matches the context filter
                            bool show = true;
                            if (tags.TryGetValue("__opid__", out var opId))
                            {
                                var mhItem = mustHaves.FirstOrDefault(m => m.Id == opId)
                                    ?? pendingAdds.FirstOrDefault(m => m.Id == opId);
                                if (mhItem != null)
                                {
                                    // Get current tags (pending edits override original)
                                    var curInsurers = pendingTagEdits.TryGetValue(opId, out var te) ? te.InsuranceCompanies : mhItem.InsuranceCompanies;
                                    var curVehicles = pendingTagEdits.TryGetValue(opId, out var tv) ? tv.VehicleTypes : mhItem.VehicleTypes;

                                    if (activeContextCategory == "insurance")
                                    {
                                        // Show if universal (no insurance tags) or matches the selected insurer
                                        show = curInsurers.Count == 0 ||
                                            curInsurers.Any(ic => ic.Equals(activeContextValue, StringComparison.OrdinalIgnoreCase));
                                    }
                                    else if (activeContextCategory == "vehicle")
                                    {
                                        show = curVehicles.Count == 0 ||
                                            curVehicles.Any(vt => vt.Equals(activeContextValue, StringComparison.OrdinalIgnoreCase));
                                    }
                                }
                            }
                            fe.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                            if (show) shownCount++;
                        }
                    }
                }
                contextInfoText.Text = $"Showing {shownCount} must-haves for {activeContextValue}";
            }

            contextCategoryBox.SelectionChanged += (s, ev) =>
            {
                PopulateContextValues();
                activeContextValue = null;
                contextValueBox.SelectedIndex = -1;
                ApplyContextFilter();
            };
            contextValueBox.SelectionChanged += (s, ev) =>
            {
                activeContextValue = contextValueBox.SelectedItem?.ToString();
                ApplyContextFilter();
            };

            contextFilterRow.Children.Add(new TextBlock
            {
                Text = "Context:",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextMuted),
                VerticalAlignment = VerticalAlignment.Center
            });
            contextFilterRow.Children.Add(contextCategoryBox);
            contextFilterRow.Children.Add(contextValueBox);
            contextFilterRow.Children.Add(contextInfoText);

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

            void UpdateSectionCount(string groupId)
            {
                if (!sectionPanels.ContainsKey(groupId)) return;
                var (panel, _, countText, _, _) = sectionPanels[groupId];
                int total = 0, checkedCount = 0;
                foreach (var (cb, info) in allCheckBoxes)
                {
                    var effectiveGroupId = info.GroupId ?? info.Section;
                    if (effectiveGroupId == groupId || info.GroupId == groupId)
                    {
                        total++;
                        if (cb.IsChecked == true) checkedCount++;
                    }
                }
                countText.Text = $"{total} items, {checkedCount} checked";
            }

            // All known operation descriptions for autocomplete (from Excel)
            var allKnownOps = new List<(string Description, decimal Price, decimal Hours)>();
            foreach (var sheetName in new[] { "SOP List", "Refinish Operations", "Cover Car Operations", "Body Operations", "Mechanical Operations" })
            {
                var ops = excel.GetSheetOperations(sheetName);
                foreach (var op in ops)
                {
                    var trimmedDesc = op.Description?.Trim() ?? "";
                    // Skip Excel placeholder rows full of zeros
                    var zeroCount = trimmedDesc.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Count(t => t == "0");
                    if (!string.IsNullOrWhiteSpace(trimmedDesc) && trimmedDesc.Length >= 4 &&
                        !trimmedDesc.StartsWith("*") && trimmedDesc.Any(char.IsLetter) &&
                        zeroCount < 3 &&
                        !trimmedDesc.Contains("\U0001f517") && !trimmedDesc.Contains("\U0001f4ca") &&
                        !trimmedDesc.Contains("\U0001f4b2") && !trimmedDesc.Contains("\U0001f6e0") &&
                        !trimmedDesc.Contains("\U0001f3a8") &&
                        !trimmedDesc.StartsWith("Back to top", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedDesc.StartsWith("Category", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedDesc.Contains("\t"))
                    {
                        allKnownOps.Add((trimmedDesc, op.Price, op.LaborHours > 0 ? op.LaborHours : op.RefinishHours));
                    }
                }
            }
            // Deduplicate autocomplete pool
            allKnownOps = allKnownOps.GroupBy(o => o.Description, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var sectionsStack = new StackPanel { Spacing = 8 };

            // === GROUP MANAGEMENT PANEL (inline, toggled) ===
            var groupMgmtPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 65, 75)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4),
                Visibility = Visibility.Collapsed
            };

            // Group list and creation controls (will be built in a helper)
            var groupMgmtContent = new StackPanel { Spacing = 8 };
            groupMgmtPanel.Child = groupMgmtContent;

            // Forward-declare rebuild so closures can reference it
            Action? rebuildSections = null;

            void BuildGroupMgmtPanel()
            {
                groupMgmtContent.Children.Clear();

                groupMgmtContent.Children.Add(new TextBlock
                {
                    Text = "Custom Groups",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(GoldAccent)
                });

                var customGroups = config.GetMustHaveGroups().Where(g => !g.IsBuiltIn).ToList();

                if (customGroups.Count == 0)
                {
                    groupMgmtContent.Children.Add(new TextBlock
                    {
                        Text = "No custom groups yet. Create one below.",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(TextDim),
                        Margin = new Thickness(0, 2, 0, 4)
                    });
                }

                foreach (var grp in customGroups)
                {
                    var grpRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // color swatch
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // count
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // up
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // down
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // rename
                    grpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete

                    var accentColor = ParseHexColor(grp.AccentColor);
                    var swatch = new Border
                    {
                        Width = 14, Height = 14,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(accentColor),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(swatch, 0);
                    grpRow.Children.Add(swatch);

                    var nameText = new TextBlock
                    {
                        Text = grp.Name,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameText, 1);
                    grpRow.Children.Add(nameText);

                    var opCount = mustHaves.Count(m => m.GroupId == grp.Id);
                    var countText = new TextBlock
                    {
                        Text = $"{opCount} ops",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(TextDim),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 8, 0)
                    };
                    Grid.SetColumn(countText, 2);
                    grpRow.Children.Add(countText);

                    var capturedGrp = grp;

                    var upBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE70E", FontSize = 9 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(TextDim),
                        MinWidth = 0, CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    upBtn.Click += (s, ev) =>
                    {
                        var allGroups = config.GetMustHaveGroups();
                        var idx = allGroups.FindIndex(g => g.Id == capturedGrp.Id);
                        if (idx > 0)
                        {
                            // Swap sort orders with previous group
                            var prev = allGroups[idx - 1];
                            (capturedGrp.SortOrder, prev.SortOrder) = (prev.SortOrder, capturedGrp.SortOrder);
                            config.UpdateMustHaveGroup(capturedGrp);
                            config.UpdateMustHaveGroup(prev);
                            BuildGroupMgmtPanel();
                            rebuildSections?.Invoke();
                        }
                    };
                    Grid.SetColumn(upBtn, 3);
                    grpRow.Children.Add(upBtn);

                    var downBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE70D", FontSize = 9 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(TextDim),
                        MinWidth = 0, CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    downBtn.Click += (s, ev) =>
                    {
                        var allGroups = config.GetMustHaveGroups();
                        var idx = allGroups.FindIndex(g => g.Id == capturedGrp.Id);
                        if (idx >= 0 && idx < allGroups.Count - 1)
                        {
                            var next = allGroups[idx + 1];
                            (capturedGrp.SortOrder, next.SortOrder) = (next.SortOrder, capturedGrp.SortOrder);
                            config.UpdateMustHaveGroup(capturedGrp);
                            config.UpdateMustHaveGroup(next);
                            BuildGroupMgmtPanel();
                            rebuildSections?.Invoke();
                        }
                    };
                    Grid.SetColumn(downBtn, 4);
                    grpRow.Children.Add(downBtn);

                    var renameBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE70F", FontSize = 9 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(TextDim),
                        MinWidth = 0, CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    ToolTipService.SetToolTip(renameBtn, "Rename group");
                    renameBtn.Click += async (s, ev) =>
                    {
                        var input = new TextBox { Text = capturedGrp.Name, FontSize = 13, Width = 300 };
                        var dlg = new ContentDialog
                        {
                            Title = "Rename Group",
                            Content = input,
                            PrimaryButtonText = "Rename",
                            CloseButtonText = "Cancel",
                            XamlRoot = xamlRoot,
                            RequestedTheme = ElementTheme.Dark
                        };
                        if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
                        {
                            capturedGrp.Name = input.Text.Trim();
                            config.UpdateMustHaveGroup(capturedGrp);
                            BuildGroupMgmtPanel();
                            rebuildSections?.Invoke();
                        }
                    };
                    Grid.SetColumn(renameBtn, 5);
                    grpRow.Children.Add(renameBtn);

                    var deleteBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE74D", FontSize = 9 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(DeleteBg),
                        Foreground = new SolidColorBrush(DeleteFg),
                        MinWidth = 0, CornerRadius = new CornerRadius(3)
                    };
                    deleteBtn.Click += async (s, ev) =>
                    {
                        var dlg = new ContentDialog
                        {
                            Title = "Delete Group",
                            Content = $"Delete \"{capturedGrp.Name}\"? Operations will move back to their default section.",
                            PrimaryButtonText = "Delete",
                            CloseButtonText = "Cancel",
                            XamlRoot = xamlRoot,
                            RequestedTheme = ElementTheme.Dark
                        };
                        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                        {
                            config.DeleteMustHaveGroup(capturedGrp.Id);
                            mustHaves = config.GetMustHaves(); // refresh after group delete unassigns ops
                            groups = config.GetMustHaveGroups();
                            BuildGroupMgmtPanel();
                            rebuildSections?.Invoke();
                        }
                    };
                    Grid.SetColumn(deleteBtn, 6);
                    grpRow.Children.Add(deleteBtn);

                    groupMgmtContent.Children.Add(grpRow);
                }

                // --- Create new group ---
                var createRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
                var newGroupName = new TextBox
                {
                    PlaceholderText = "New group name...",
                    Width = 180, FontSize = 12
                };
                createRow.Children.Add(newGroupName);

                // Color swatch picker
                string selectedColor = GroupColorSwatches[0];
                var swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                var swatchBorders = new List<Border>();
                foreach (var hexColor in GroupColorSwatches)
                {
                    var capturedHex = hexColor;
                    var swatchBorder = new Border
                    {
                        Width = 18, Height = 18,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(ParseHexColor(hexColor)),
                        BorderThickness = new Thickness(hexColor == selectedColor ? 2 : 0),
                        BorderBrush = new SolidColorBrush(Colors.White),
                        Margin = new Thickness(0)
                    };
                    swatchBorder.Tapped += (s, ev) =>
                    {
                        selectedColor = capturedHex;
                        foreach (var sb in swatchBorders)
                            sb.BorderThickness = new Thickness(0);
                        if (s is Border b)
                            b.BorderThickness = new Thickness(2);
                    };
                    swatchBorders.Add(swatchBorder);
                    swatchRow.Children.Add(swatchBorder);
                }
                createRow.Children.Add(swatchRow);

                var createBtn = new Button
                {
                    Content = "Create",
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromArgb(255, 40, 80, 60)),
                    Foreground = new SolidColorBrush(GreenAccent),
                    Padding = new Thickness(10, 4, 10, 4),
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                createBtn.Click += (s, ev) =>
                {
                    if (string.IsNullOrWhiteSpace(newGroupName.Text)) return;
                    var newGroup = new MustHaveGroup
                    {
                        Name = newGroupName.Text.Trim(),
                        AccentColor = selectedColor,
                        IsBuiltIn = false
                    };
                    config.AddMustHaveGroup(newGroup);
                    groups = config.GetMustHaveGroups();
                    BuildGroupMgmtPanel();
                    rebuildSections?.Invoke();
                };
                createRow.Children.Add(createBtn);

                groupMgmtContent.Children.Add(createRow);
            }

            // === BUILD GROUP-BASED SECTIONS ===
            void BuildSections()
            {
                sectionsStack.Children.Clear();
                allCheckBoxes.Clear();
                sectionPanels.Clear();

                groups = config.GetMustHaveGroups();
                mustHaves = config.GetMustHaves();

                foreach (var group in groups)
                {
                    var accent = ParseHexColor(group.AccentColor);

                    // Find operations for this group: GroupId match, or Section fallback
                    var groupOps = mustHaves.Where(m =>
                        !pendingDeletes.Contains(m.Id) &&
                        (m.GroupId == group.Id || (m.GroupId == null && m.Section.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
                    ).ToList();

                    // Include pending adds assigned to this group
                    var pendingGroupOps = pendingAdds.Where(m =>
                        m.GroupId == group.Id || (m.GroupId == null && m.Section.Equals(group.Name, StringComparison.OrdinalIgnoreCase))
                    ).ToList();

                    // Hide empty built-in groups; always show custom groups
                    if (group.IsBuiltIn && groupOps.Count == 0 && pendingGroupOps.Count == 0)
                        continue;

                    var sectionItemsPanel = new StackPanel { Spacing = 2 };

                    // === Section header ===
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: Chevron
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1: Label
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: Count
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: Add "+"
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: Check All

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
                        Text = group.Name,
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

                    // Per-section "+" add button
                    var addSectionBtn = new Button
                    {
                        Content = new FontIcon { Glyph = "\uE710", FontSize = 10 },
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(accent),
                        CornerRadius = new CornerRadius(3),
                        MinWidth = 0,
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTipService.SetToolTip(addSectionBtn, "Add operation to this group");

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
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    Grid.SetColumn(chevron, 0);
                    Grid.SetColumn(headerLabel, 1);
                    Grid.SetColumn(countLabel, 2);
                    Grid.SetColumn(addSectionBtn, 3);
                    Grid.SetColumn(checkAllBtn, 4);
                    headerGrid.Children.Add(chevron);
                    headerGrid.Children.Add(headerLabel);
                    headerGrid.Children.Add(countLabel);
                    headerGrid.Children.Add(addSectionBtn);
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

                    // Inline add form (hidden by default)
                    var addFormBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 30, 38, 44)),
                        Padding = new Thickness(10, 6, 10, 6),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(8, 4, 0, 4),
                        Visibility = Visibility.Collapsed
                    };

                    var addFormStack = new StackPanel { Spacing = 6 };

                    // Add Row 1: Op Type + Description
                    var addRow1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    var addOpTypeBox = new ComboBox { Header = "Op Type", Width = 130, FontSize = 11, SelectedIndex = 0 };
                    foreach (var (shortName, longName) in CccOpTypes)
                        addOpTypeBox.Items.Add($"{shortName} - {longName}");

                    var addNameBox = new AutoSuggestBox
                    {
                        PlaceholderText = "Operation name...",
                        Width = 200, FontSize = 12
                    };
                    addNameBox.TextChanged += (s, args) =>
                    {
                        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                        {
                            var query = addNameBox.Text?.ToLowerInvariant() ?? "";
                            if (query.Length >= 2)
                            {
                                addNameBox.ItemsSource = allKnownOps
                                    .Where(o => o.Description.ToLowerInvariant().Contains(query))
                                    .Select(o => o.Description).Distinct().Take(10).ToList();
                            }
                            else addNameBox.ItemsSource = null;
                        }
                    };
                    addRow1.Children.Add(addOpTypeBox);
                    addRow1.Children.Add(addNameBox);
                    addFormStack.Children.Add(addRow1);

                    // Add Row 2: Qty, Price, Body Hours, Labor Category, Refinish Hours
                    var addRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    var addQtyBox = new NumberBox { Header = "Qty", Minimum = 1, Maximum = 99, SmallChange = 1, Value = 1, Width = 60, FontSize = 11 };
                    var addPriceBox = new NumberBox { PlaceholderText = "$", Minimum = 0, Maximum = 50000, Width = 70, FontSize = 11, Header = "Price" };
                    var addBodyHoursBox = new NumberBox { PlaceholderText = "hrs", Minimum = 0, Maximum = 50, SmallChange = 0.1, Width = 70, FontSize = 11, Header = "Body Hrs" };
                    var addLaborCatBox = new ComboBox { Header = "Labor Cat", Width = 130, FontSize = 11, SelectedIndex = 0 };
                    foreach (var cat in BodyLaborCategories)
                        addLaborCatBox.Items.Add(cat);
                    var addRfnHoursBox = new NumberBox { PlaceholderText = "hrs", Minimum = 0, Maximum = 50, SmallChange = 0.1, Width = 70, FontSize = 11, Header = "Rfn Hrs" };
                    addRow2.Children.Add(addQtyBox);
                    addRow2.Children.Add(addPriceBox);
                    addRow2.Children.Add(addBodyHoursBox);
                    addRow2.Children.Add(addLaborCatBox);
                    addRow2.Children.Add(addRfnHoursBox);
                    addFormStack.Children.Add(addRow2);

                    // Add Row 3: Condition + Add button
                    var addRow3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    var addCondBox = new ComboBox { IsEditable = true, Width = 120, FontSize = 11, Header = "Condition", SelectedIndex = 0 };
                    foreach (var cond in EstimateConditionEvaluator.AllConditions)
                        addCondBox.Items.Add(cond);

                    // Auto-fill from autocomplete selection
                    addNameBox.SuggestionChosen += (s, args) =>
                    {
                        var chosen = args.SelectedItem?.ToString() ?? "";
                        var match = allKnownOps.FirstOrDefault(o => o.Description.Equals(chosen, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.Description))
                        {
                            if (match.Price > 0) addPriceBox.Value = (double)match.Price;
                            if (match.Hours > 0) addBodyHoursBox.Value = (double)match.Hours;
                        }
                    };

                    var capturedGroup = group;
                    var capturedSectionItems = sectionItemsPanel;
                    var capturedAddForm = addFormBorder;

                    var addConfirmBtn = new Button
                    {
                        Content = "Add",
                        FontSize = 11,
                        Background = new SolidColorBrush(Color.FromArgb(255, 40, 80, 60)),
                        Foreground = new SolidColorBrush(GreenAccent),
                        Padding = new Thickness(10, 4, 10, 4),
                        CornerRadius = new CornerRadius(4),
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    addConfirmBtn.Click += (s, ev) =>
                    {
                        if (string.IsNullOrWhiteSpace(addNameBox.Text)) return;
                        var trimmedName = addNameBox.Text.Trim();
                        if (trimmedName.StartsWith("*") || !trimmedName.Any(char.IsLetter)) return;

                        var selOpType = addOpTypeBox.SelectedIndex >= 0 && addOpTypeBox.SelectedIndex < CccOpTypes.Length
                            ? CccOpTypes[addOpTypeBox.SelectedIndex].Short : "";
                        var qty = !double.IsNaN(addQtyBox.Value) ? (int)addQtyBox.Value : 1;
                        var price = !double.IsNaN(addPriceBox.Value) ? (decimal)addPriceBox.Value : 0;
                        var bodyHours = !double.IsNaN(addBodyHoursBox.Value) ? (decimal)addBodyHoursBox.Value : 0;
                        var laborCat = addLaborCatBox.SelectedIndex >= 0 && addLaborCatBox.SelectedIndex < BodyLaborCategories.Length
                            ? BodyLaborCategories[addLaborCatBox.SelectedIndex] : "Body";
                        var rfnHours = !double.IsNaN(addRfnHoursBox.Value) ? (decimal)addRfnHoursBox.Value : 0;
                        var selectedCond = addCondBox.SelectedItem?.ToString() ?? addCondBox.Text ?? "always";
                        if (string.IsNullOrWhiteSpace(selectedCond)) selectedCond = "always";

                        var newMh = new MustHaveOperation
                        {
                            Description = trimmedName,
                            Section = capturedGroup.Name,
                            GroupId = capturedGroup.Id,
                            CccOperationType = selOpType,
                            Quantity = qty,
                            ExpectedPrice = price,
                            ExpectedHours = bodyHours,
                            BodyLaborCategory = laborCat,
                            RefinishHours = rfnHours,
                            Conditions = selectedCond,
                            Enabled = true
                        };
                        pendingAdds.Add(newMh);

                        // Add row to current section
                        AddOperationRow(newMh, capturedGroup, accent, capturedSectionItems, allCheckBoxes,
                            pendingEdits, pendingDeletes, groups, () => { UpdateSectionCount(capturedGroup.Id); UpdateSummary(); }, pendingTagEdits);

                        addNameBox.Text = "";
                        addOpTypeBox.SelectedIndex = 0;
                        addQtyBox.Value = 1;
                        addPriceBox.Value = double.NaN;
                        addBodyHoursBox.Value = double.NaN;
                        addLaborCatBox.SelectedIndex = 0;
                        addRfnHoursBox.Value = double.NaN;
                        addCondBox.SelectedIndex = 0;
                        UpdateSectionCount(capturedGroup.Id);
                        UpdateSummary();
                    };

                    addRow3.Children.Add(addCondBox);
                    addRow3.Children.Add(addConfirmBtn);
                    addFormStack.Children.Add(addRow3);
                    addFormBorder.Child = addFormStack;

                    // Toggle add form
                    addSectionBtn.Click += (s, ev) =>
                    {
                        capturedAddForm.Visibility = capturedAddForm.Visibility == Visibility.Visible
                            ? Visibility.Collapsed : Visibility.Visible;
                    };

                    // Toggle collapse on header click (not on buttons)
                    var capturedChevron = chevron;
                    var capturedGroupId = group.Id;
                    headerBorder.Tapped += (s, ev) =>
                    {
                        if (sectionPanels.ContainsKey(capturedGroupId))
                        {
                            var p = sectionPanels[capturedGroupId].Panel;
                            var af = sectionPanels[capturedGroupId].AddFormBorder;
                            bool isVisible = p.Visibility == Visibility.Visible;
                            p.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                            if (af != null && isVisible) af.Visibility = Visibility.Collapsed;
                            capturedChevron.Text = isVisible ? "\uE76C" : "\uE70D";
                        }
                    };

                    sectionPanels[group.Id] = (sectionItemsPanel, headerBorder, countLabel, checkAllBtn, addFormBorder);

                    // Check All button
                    checkAllBtn.Click += (s, ev) =>
                    {
                        var sectionCbs = allCheckBoxes
                            .Where(kv => kv.Value.GroupId == capturedGroupId ||
                                         (kv.Value.GroupId == null && kv.Value.Section.Equals(capturedGroup.Name, StringComparison.OrdinalIgnoreCase)))
                            .Select(kv => kv.Key).ToList();
                        bool allChecked = sectionCbs.All(cb => cb.IsChecked == true);
                        foreach (var cb in sectionCbs)
                            cb.IsChecked = !allChecked;
                        UpdateSectionCount(capturedGroupId);
                        UpdateSummary();
                    };

                    sectionsStack.Children.Add(headerBorder);
                    sectionsStack.Children.Add(addFormBorder);
                    sectionsStack.Children.Add(sectionItemsPanel);

                    // Add operation rows
                    foreach (var mh in groupOps)
                    {
                        AddOperationRow(mh, group, accent, sectionItemsPanel, allCheckBoxes,
                            pendingEdits, pendingDeletes, groups, () => { UpdateSectionCount(group.Id); UpdateSummary(); }, pendingTagEdits);
                    }
                    foreach (var mh in pendingGroupOps)
                    {
                        AddOperationRow(mh, group, accent, sectionItemsPanel, allCheckBoxes,
                            pendingEdits, pendingDeletes, groups, () => { UpdateSectionCount(group.Id); UpdateSummary(); }, pendingTagEdits);
                    }
                }

                // Update all counts
                foreach (var gId in sectionPanels.Keys)
                    UpdateSectionCount(gId);
                UpdateSummary();
            }

            rebuildSections = BuildSections;
            BuildSections();
            BuildGroupMgmtPanel();

            // === SEARCH LOGIC ===
            // Map filter dropdown index to search tag key
            var filterKeys = new[] { "all", "name", "optype", "laborcat", "condition", "group" };
            string? activeSidebarFilter = null; // null = show all groups; else only the matching group

            void RunSearch()
            {
                var query = searchBox.Text?.ToLowerInvariant() ?? "";
                var filterIdx = searchFilterBox.SelectedIndex;
                var filterKey = filterIdx >= 0 && filterIdx < filterKeys.Length ? filterKeys[filterIdx] : "all";

                foreach (var (groupId, (panel, header, countText, checkAll, addForm)) in sectionPanels)
                {
                    int visibleCount = 0;
                    foreach (var child in panel.Children)
                    {
                        if (child is FrameworkElement fe && fe.Tag is Dictionary<string, string> tags)
                        {
                            bool matches = string.IsNullOrEmpty(query);
                            if (!matches && tags.TryGetValue(filterKey, out var searchable))
                                matches = searchable.Contains(query);
                            child.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                            if (matches) visibleCount++;
                        }
                    }
                    bool groupPassesSidebar = activeSidebarFilter == null || activeSidebarFilter == groupId;
                    bool showGroup = groupPassesSidebar && (string.IsNullOrEmpty(query) || visibleCount > 0);
                    header.Visibility = showGroup ? Visibility.Visible : Visibility.Collapsed;
                    panel.Visibility = showGroup ? Visibility.Visible : Visibility.Collapsed;
                    if (addForm != null && !showGroup) addForm.Visibility = Visibility.Collapsed;
                }
            }

            searchBox.TextChanged += (s, args) => RunSearch();
            searchFilterBox.SelectionChanged += (s, args) => RunSearch();

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
                    var matching = allCheckBoxes.Where(kv =>
                    {
                        var descLower = kv.Value.Description.ToLowerInvariant();
                        return capturedKeywords.Any(kw => descLower.Contains(kw));
                    }).Select(kv => kv.Key).ToList();

                    if (isActive)
                    {
                        foreach (var cb in matching) cb.IsChecked = false;
                        activePresets.Remove(capturedName);
                        presetBtn.Background = new SolidColorBrush(BgCard);
                        presetBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 65, 75));
                    }
                    else
                    {
                        foreach (var cb in matching) cb.IsChecked = true;
                        activePresets.Add(capturedName);
                        presetBtn.Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 80));
                        presetBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 140, 200));
                    }

                    foreach (var gId in sectionPanels.Keys)
                        UpdateSectionCount(gId);
                    UpdateSummary();
                };

                presetRow.Children.Add(presetBtn);
            }

            // "Manage Groups" button
            var manageGroupsBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE713", FontSize = 10 },
                        new TextBlock { Text = "Groups", FontSize = 11 }
                    }
                },
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 40, 30)),
                Foreground = new SolidColorBrush(GoldAccent),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 200, 100)),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(12)
            };
            manageGroupsBtn.Click += (s, ev) =>
            {
                groupMgmtPanel.Visibility = groupMgmtPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                if (groupMgmtPanel.Visibility == Visibility.Visible)
                    BuildGroupMgmtPanel();
            };
            presetRow.Children.Add(manageGroupsBtn);

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

                        foreach (var gId in sectionPanels.Keys)
                            UpdateSectionCount(gId);
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

            // === SIDEBAR (group filter navigation) ===
            var sidebarStack = new StackPanel { Spacing = 2 };
            var sidebarButtons = new Dictionary<string, (Border Border, TextBlock CountText)>();

            void HighlightSidebar()
            {
                foreach (var (gid, (b, _)) in sidebarButtons)
                {
                    bool isActive = (activeSidebarFilter == null && gid == "__all__") || activeSidebarFilter == gid;
                    b.Background = new SolidColorBrush(isActive
                        ? Color.FromArgb(255, 50, 60, 75)
                        : Color.FromArgb(255, 30, 35, 42));
                    b.BorderThickness = new Thickness(isActive ? 2 : 0, 0, 0, 0);
                }
            }

            Border BuildSidebarItem(string id, string label, Color accent, int count)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new Border
                {
                    Width = 10, Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(dot, 0);
                grid.Children.Add(dot);

                var lblText = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(lblText, 1);
                grid.Children.Add(lblText);

                var countText = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextDim),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(countText, 2);
                grid.Children.Add(countText);

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 30, 35, 42)),
                    Padding = new Thickness(10, 7, 10, 7),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(0),
                    Child = grid,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                border.Tapped += (s, ev) =>
                {
                    activeSidebarFilter = id == "__all__" ? null : id;
                    HighlightSidebar();
                    RunSearch();
                };

                sidebarButtons[id] = (border, countText);
                return border;
            }

            void BuildSidebar()
            {
                sidebarStack.Children.Clear();
                sidebarButtons.Clear();

                sidebarStack.Children.Add(new TextBlock
                {
                    Text = "GROUPS",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextDim),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(4, 2, 0, 6)
                });

                int totalCount = mustHaves.Count(m => !pendingDeletes.Contains(m.Id)) + pendingAdds.Count;
                sidebarStack.Children.Add(BuildSidebarItem("__all__", "All", Color.FromArgb(255, 100, 180, 255), totalCount));

                foreach (var grp in groups)
                {
                    var grpOps = mustHaves.Count(m =>
                        !pendingDeletes.Contains(m.Id) &&
                        (m.GroupId == grp.Id || (m.GroupId == null && m.Section.Equals(grp.Name, StringComparison.OrdinalIgnoreCase))));
                    grpOps += pendingAdds.Count(m =>
                        m.GroupId == grp.Id || (m.GroupId == null && m.Section.Equals(grp.Name, StringComparison.OrdinalIgnoreCase)));
                    if (grp.IsBuiltIn && grpOps == 0) continue;
                    sidebarStack.Children.Add(BuildSidebarItem(grp.Id, grp.Name, ParseHexColor(grp.AccentColor), grpOps));
                }

                HighlightSidebar();
            }
            BuildSidebar();

            // Wrap the existing rebuildSections so sidebar stays in sync when groups/ops change
            var originalRebuild = rebuildSections;
            rebuildSections = () =>
            {
                originalRebuild?.Invoke();
                BuildSidebar();
                RunSearch();
            };

            // === ASSEMBLE DIALOG (wide: preset/template/search on top, sidebar + content below) ===
            dialogStack.MinWidth = 1280;
            Grid.SetRow(presetRow, _dialogRow++);
            dialogStack.Children.Add(presetRow);
            Grid.SetRow(userTemplateRow, _dialogRow++);
            dialogStack.Children.Add(userTemplateRow);
            Grid.SetRow(groupMgmtPanel, _dialogRow++);
            dialogStack.Children.Add(groupMgmtPanel);
            Grid.SetRow(searchRow, _dialogRow++);
            dialogStack.Children.Add(searchRow);
            Grid.SetRow(contextFilterRow, _dialogRow++);
            dialogStack.Children.Add(contextFilterRow);

            scrollViewer.Content = sectionsStack;

            var sidebarScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sidebarStack
            };

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sidebarBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 28, 34)),
                Padding = new Thickness(8, 10, 8, 10),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = sidebarScroll
            };
            Grid.SetColumn(sidebarBorder, 0);
            contentGrid.Children.Add(sidebarBorder);

            Grid.SetColumn(scrollViewer, 1);
            contentGrid.Children.Add(scrollViewer);

            Grid.SetRow(contentGrid, _dialogRow++);
            dialogStack.Children.Add(contentGrid);
            Grid.SetRow(summaryText, _dialogRow++);
            dialogStack.Children.Add(summaryText);

            var dialog = new ContentDialog
            {
                Title = "Must-Haves \u2014 Required on Every Estimate",
                Content = dialogStack,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark
            };
            // Widen the dialog's max width to fit the sidebar + compact rows
            dialog.Resources["ContentDialogMaxWidth"] = 1400.0;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SaveMustHaves(config, allCheckBoxes, pendingEdits, pendingDeletes, pendingAdds, pendingTagEdits);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Add a single operation row to a section panel.
        /// </summary>
        private static void AddOperationRow(
            MustHaveOperation mh,
            MustHaveGroup group,
            Color accent,
            StackPanel sectionPanel,
            Dictionary<CheckBox, (string Id, string Description, string Section, string? GroupId, decimal Price, decimal Hours)> allCheckBoxes,
            Dictionary<CheckBox, (string Description, string CccOpType, int Quantity, decimal Price, decimal BodyHours, string BodyLaborCategory, decimal RefinishHours, string Condition, string? GroupId)> pendingEdits,
            HashSet<string> pendingDeletes,
            List<MustHaveGroup> groups,
            Action updateCounts,
            Dictionary<string, (List<string> InsuranceCompanies, List<string> VehicleTypes)>? pendingTagEdits = null)
        {
            var opPrice = mh.ExpectedPrice;
            var opHours = mh.ExpectedHours;
            var condition = mh.Conditions ?? "always";

            // Single compact horizontal row - all fields inline
            var rowWrapper = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(4, 1, 4, 1),
                Padding = new Thickness(6, 3, 6, 3)
            };
            // Subtle row background for alternating feel via border
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
                CornerRadius = new CornerRadius(3),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Margin = new Thickness(8, 1, 0, 1),
                Child = rowWrapper
            };

            var cb = new CheckBox
            {
                IsChecked = mh.Enabled,
                MinWidth = 0,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = mh.Description.ToLowerInvariant()
            };
            cb.Checked += (s, ev) => updateCounts();
            cb.Unchecked += (s, ev) => updateCounts();
            rowWrapper.Children.Add(cb);

            // Operation Name (editable, primary display)
            var fldName = new TextBox
            {
                Text = mh.Description, FontSize = 11, Width = 220,
                Padding = new Thickness(6, 3, 6, 3),
                PlaceholderText = "Operation name...",
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(fldName, "Operation Name");
            rowWrapper.Children.Add(fldName);

            // Op Type
            var fldOpType = new ComboBox { FontSize = 10, MinWidth = 0, Width = 95, Padding = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            int selectedOpIdx = 0;
            for (int i = 0; i < CccOpTypes.Length; i++)
            {
                fldOpType.Items.Add(CccOpTypes[i].Short);
                if (CccOpTypes[i].Short.Equals(mh.CccOperationType, StringComparison.OrdinalIgnoreCase))
                    selectedOpIdx = i;
            }
            fldOpType.SelectedIndex = selectedOpIdx;
            ToolTipService.SetToolTip(fldOpType, "Operation Type");
            rowWrapper.Children.Add(fldOpType);

            // Qty
            var fldQty = new NumberBox
            {
                Minimum = 1, Maximum = 99, SmallChange = 1,
                Value = mh.Quantity > 0 ? mh.Quantity : 1,
                Width = 55, FontSize = 10,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(fldQty, "Quantity");
            rowWrapper.Children.Add(fldQty);

            // Price
            var fldPrice = new NumberBox
            {
                Minimum = 0, Maximum = 50000,
                Value = opPrice > 0 ? (double)opPrice : double.NaN,
                Width = 70, FontSize = 10, PlaceholderText = "$",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(fldPrice, "Price $");
            rowWrapper.Children.Add(fldPrice);

            // Body Hours
            var fldBodyHrs = new NumberBox
            {
                Minimum = 0, Maximum = 50, SmallChange = 0.1,
                Value = opHours > 0 ? (double)opHours : double.NaN,
                Width = 60, FontSize = 10, PlaceholderText = "body",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(fldBodyHrs, "Body Hours");
            rowWrapper.Children.Add(fldBodyHrs);

            // Labor Category
            var fldLaborCat = new ComboBox { FontSize = 10, MinWidth = 0, Width = 115, Padding = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            int selectedCatIdx = 0;
            for (int i = 0; i < BodyLaborCategories.Length; i++)
            {
                fldLaborCat.Items.Add(BodyLaborCategories[i]);
                if (BodyLaborCategories[i].Equals(mh.BodyLaborCategory, StringComparison.OrdinalIgnoreCase))
                    selectedCatIdx = i;
            }
            fldLaborCat.SelectedIndex = selectedCatIdx;
            ToolTipService.SetToolTip(fldLaborCat, "Body Labor Category");
            rowWrapper.Children.Add(fldLaborCat);

            // Refinish Hours
            var fldRfnHrs = new NumberBox
            {
                Minimum = 0, Maximum = 50, SmallChange = 0.1,
                Value = mh.RefinishHours > 0 ? (double)mh.RefinishHours : double.NaN,
                Width = 60, FontSize = 10, PlaceholderText = "rfn",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(fldRfnHrs, "Refinish Hours");
            rowWrapper.Children.Add(fldRfnHrs);

            // Condition
            var fldCond = new ComboBox { FontSize = 10, IsEditable = true, MinWidth = 0, Width = 110, Padding = new Thickness(6, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
            foreach (var cond in EstimateConditionEvaluator.AllConditions)
                fldCond.Items.Add(cond);
            fldCond.SelectedItem = condition;
            ToolTipService.SetToolTip(fldCond, "Condition");
            rowWrapper.Children.Add(fldCond);

            // Condition tag (hidden - shown next to name when non-default, via text change)
            var condTag = new TextBlock
            {
                Text = "",
                FontSize = 9,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            // === TAG PILLS (Insurance Companies + Vehicle Types) ===
            var tagPillsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };

            void RebuildTagPills()
            {
                tagPillsPanel.Children.Clear();
                var curInsurers = pendingTagEdits != null && pendingTagEdits.TryGetValue(mh.Id, out var te)
                    ? te.InsuranceCompanies : mh.InsuranceCompanies;
                var curVehicles = pendingTagEdits != null && pendingTagEdits.TryGetValue(mh.Id, out var tv)
                    ? tv.VehicleTypes : mh.VehicleTypes;

                foreach (var ins in curInsurers)
                {
                    tagPillsPanel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 70)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 1, 6, 1),
                        Child = new TextBlock
                        {
                            Text = ins,
                            FontSize = 9,
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 170, 230))
                        }
                    });
                }
                foreach (var vt in curVehicles)
                {
                    var vtColor = vt.Contains("Electric") ? Color.FromArgb(255, 40, 70, 50) :
                                  vt.Contains("Hybrid") ? Color.FromArgb(255, 60, 55, 30) :
                                  Color.FromArgb(255, 50, 50, 50);
                    var vtFg = vt.Contains("Electric") ? Color.FromArgb(255, 120, 220, 140) :
                               vt.Contains("Hybrid") ? Color.FromArgb(255, 230, 200, 100) :
                               Color.FromArgb(255, 180, 180, 180);
                    tagPillsPanel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(vtColor),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(6, 1, 6, 1),
                        Child = new TextBlock { Text = vt, FontSize = 9, Foreground = new SolidColorBrush(vtFg) }
                    });
                }
                if (curInsurers.Count == 0 && curVehicles.Count == 0)
                {
                    tagPillsPanel.Children.Add(new TextBlock
                    {
                        Text = "All",
                        FontSize = 8,
                        Foreground = new SolidColorBrush(TextDim),
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.6
                    });
                }
            }
            RebuildTagPills();
            rowWrapper.Children.Add(tagPillsPanel);

            // Tag edit button
            var tagEditBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE1CB", FontSize = 9 },
                Padding = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(TextDim),
                CornerRadius = new CornerRadius(3),
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7
            };
            ToolTipService.SetToolTip(tagEditBtn, "Edit insurance/vehicle tags");
            rowWrapper.Children.Add(tagEditBtn);

            tagEditBtn.Click += (s, ev) =>
            {
                // Build a flyout with checkboxes for insurers and vehicle types
                var flyoutPanel = new StackPanel { Spacing = 6, Padding = new Thickness(4), MinWidth = 200 };

                flyoutPanel.Children.Add(new TextBlock
                {
                    Text = "INSURANCE COMPANIES",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 170, 230))
                });

                var curInsurers = pendingTagEdits != null && pendingTagEdits.TryGetValue(mh.Id, out var te2)
                    ? new List<string>(te2.InsuranceCompanies) : new List<string>(mh.InsuranceCompanies);
                var curVehicles = pendingTagEdits != null && pendingTagEdits.TryGetValue(mh.Id, out var tv2)
                    ? new List<string>(tv2.VehicleTypes) : new List<string>(mh.VehicleTypes);

                // Known insurers from history + already tagged
                var allInsurers = EstimateHistoryDatabase.Instance.KnownInsurers
                    .Concat(curInsurers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s).ToList();

                var insurerChecks = new Dictionary<string, CheckBox>();
                foreach (var ins in allInsurers)
                {
                    var insCheck = new CheckBox
                    {
                        Content = ins,
                        FontSize = 11,
                        IsChecked = curInsurers.Any(i => i.Equals(ins, StringComparison.OrdinalIgnoreCase)),
                        MinWidth = 0, Padding = new Thickness(0)
                    };
                    insurerChecks[ins] = insCheck;
                    flyoutPanel.Children.Add(insCheck);
                }

                if (allInsurers.Count == 0)
                {
                    flyoutPanel.Children.Add(new TextBlock
                    {
                        Text = "No insurers in database yet",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(TextDim),
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    });
                }

                flyoutPanel.Children.Add(new Border
                {
                    Height = 1, Margin = new Thickness(0, 4, 0, 4),
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                });

                flyoutPanel.Children.Add(new TextBlock
                {
                    Text = "VEHICLE TYPE",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 220, 140))
                });

                var vehicleChecks = new Dictionary<string, CheckBox>();
                foreach (var vt in GhostConfigService.KnownVehicleFuelTypes)
                {
                    var vtCheck = new CheckBox
                    {
                        Content = vt,
                        FontSize = 11,
                        IsChecked = curVehicles.Any(v => v.Equals(vt, StringComparison.OrdinalIgnoreCase)),
                        MinWidth = 0, Padding = new Thickness(0)
                    };
                    vehicleChecks[vt] = vtCheck;
                    flyoutPanel.Children.Add(vtCheck);
                }

                flyoutPanel.Children.Add(new TextBlock
                {
                    Text = "Unchecked = applies to all",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextDim),
                    Margin = new Thickness(0, 4, 0, 0)
                });

                var flyout = new Flyout
                {
                    Content = flyoutPanel,
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
                };
                flyout.Closed += (fs, fe) =>
                {
                    // Collect checked insurers
                    var newInsurers = insurerChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                    var newVehicles = vehicleChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                    pendingTagEdits ??= new();
                    pendingTagEdits[mh.Id] = (newInsurers, newVehicles);
                    RebuildTagPills();
                };
                flyout.ShowAt(tagEditBtn);
            };

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 9 },
                Padding = new Thickness(5, 3, 5, 3),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(DeleteFg),
                CornerRadius = new CornerRadius(3),
                MinWidth = 0,
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6
            };
            ToolTipService.SetToolTip(deleteBtn, "Delete operation");
            rowWrapper.Children.Add(deleteBtn);

            // Capture for closures
            var capturedCb = cb;
            var capturedCondTag = condTag;
            var capturedRowWrapper = rowBorder;
            var capturedMh = mh;

            // Helper: read current field values and update pendingEdits
            void SyncPendingEdit()
            {
                var curDesc = fldName.Text?.Trim();
                if (string.IsNullOrWhiteSpace(curDesc)) curDesc = capturedMh.Description;

                // Keep checkbox tag in sync with name field (used for filtering)
                capturedCb.Tag = curDesc.ToLowerInvariant();

                var curOpType = fldOpType.SelectedIndex >= 0 && fldOpType.SelectedIndex < CccOpTypes.Length
                    ? CccOpTypes[fldOpType.SelectedIndex].Short : "";
                var curQty = !double.IsNaN(fldQty.Value) ? (int)fldQty.Value : 1;
                var curPrice = !double.IsNaN(fldPrice.Value) ? (decimal)fldPrice.Value : 0;
                var curBodyHrs = !double.IsNaN(fldBodyHrs.Value) ? (decimal)fldBodyHrs.Value : 0;
                var curLaborCat = fldLaborCat.SelectedIndex >= 0 && fldLaborCat.SelectedIndex < BodyLaborCategories.Length
                    ? BodyLaborCategories[fldLaborCat.SelectedIndex] : "Body";
                var curRfnHrs = !double.IsNaN(fldRfnHrs.Value) ? (decimal)fldRfnHrs.Value : 0;
                var curCond = fldCond.SelectedItem?.ToString() ?? fldCond.Text ?? "always";
                if (string.IsNullOrWhiteSpace(curCond)) curCond = "always";

                // Update condition tag display
                if (!string.IsNullOrEmpty(curCond) && curCond != "always")
                {
                    capturedCondTag.Text = $"[{curCond}]";
                    capturedCondTag.Visibility = Visibility.Visible;
                }
                else
                {
                    capturedCondTag.Text = "";
                    capturedCondTag.Visibility = Visibility.Collapsed;
                }

                // Find current group (stays same group since no group picker in inline row)
                string? curGroupId = group.Id;

                allCheckBoxes[capturedCb] = (capturedMh.Id, curDesc, capturedMh.Section, curGroupId, curPrice, curBodyHrs);
                pendingEdits[capturedCb] = (curDesc, curOpType, curQty, curPrice, curBodyHrs, curLaborCat, curRfnHrs, curCond, curGroupId);
            }

            // Auto-sync on any field change
            fldName.TextChanged += (s, ev) => SyncPendingEdit();
            fldOpType.SelectionChanged += (s, ev) => SyncPendingEdit();
            fldQty.ValueChanged += (s, ev) => SyncPendingEdit();
            fldPrice.ValueChanged += (s, ev) => SyncPendingEdit();
            fldBodyHrs.ValueChanged += (s, ev) => SyncPendingEdit();
            fldLaborCat.SelectionChanged += (s, ev) => SyncPendingEdit();
            fldRfnHrs.ValueChanged += (s, ev) => SyncPendingEdit();
            fldCond.SelectionChanged += (s, ev) => SyncPendingEdit();

            deleteBtn.Click += (s, ev) =>
            {
                capturedCb.IsChecked = false;
                capturedRowWrapper.Visibility = Visibility.Collapsed;
                allCheckBoxes.Remove(capturedCb);
                pendingEdits.Remove(capturedCb);
                pendingDeletes.Add(capturedMh.Id);
                updateCounts();
            };

            // Store searchable metadata on wrapper for filtering
            void UpdateSearchTag()
            {
                var name = fldName.Text?.ToLowerInvariant() ?? "";
                var opType = fldOpType.SelectedIndex >= 0 && fldOpType.SelectedIndex < CccOpTypes.Length
                    ? (CccOpTypes[fldOpType.SelectedIndex].Short + " " + CccOpTypes[fldOpType.SelectedIndex].Long).ToLowerInvariant() : "";
                var laborCat = fldLaborCat.SelectedIndex >= 0 && fldLaborCat.SelectedIndex < BodyLaborCategories.Length
                    ? BodyLaborCategories[fldLaborCat.SelectedIndex].ToLowerInvariant() : "";
                var cond = (fldCond.SelectedItem?.ToString() ?? fldCond.Text ?? "").ToLowerInvariant();
                var grpName = group.Name.ToLowerInvariant();

                rowBorder.Tag = new Dictionary<string, string>
                {
                    ["all"] = $"{name} {opType} {laborCat} {cond} {grpName}",
                    ["name"] = name,
                    ["optype"] = opType,
                    ["laborcat"] = laborCat,
                    ["condition"] = cond,
                    ["group"] = grpName,
                    ["__opid__"] = mh.Id
                };
            }
            UpdateSearchTag();

            // Also update search tag when fields change
            fldName.TextChanged += (s, ev) => UpdateSearchTag();
            fldOpType.SelectionChanged += (s, ev) => UpdateSearchTag();
            fldLaborCat.SelectionChanged += (s, ev) => UpdateSearchTag();
            fldCond.SelectionChanged += (s, ev) => UpdateSearchTag();

            sectionPanel.Children.Add(rowBorder);
            allCheckBoxes[cb] = (mh.Id, mh.Description, mh.Section, mh.GroupId ?? group.Id, opPrice, opHours);
        }

        private static void SaveMustHaves(
            GhostConfigService config,
            Dictionary<CheckBox, (string Id, string Description, string Section, string? GroupId, decimal Price, decimal Hours)> checkBoxMap,
            Dictionary<CheckBox, (string Description, string CccOpType, int Quantity, decimal Price, decimal BodyHours, string BodyLaborCategory, decimal RefinishHours, string Condition, string? GroupId)> pendingEdits,
            HashSet<string> pendingDeletes,
            List<MustHaveOperation> pendingAdds,
            Dictionary<string, (List<string> InsuranceCompanies, List<string> VehicleTypes)>? pendingTagEdits = null)
        {
            // 1. Apply deletes
            foreach (var id in pendingDeletes)
                config.RemoveMustHave(id);

            // 2. Apply edits and enabled/disabled state
            foreach (var (cb, info) in checkBoxMap)
            {
                if (pendingDeletes.Contains(info.Id)) continue;

                var existing = config.GetMustHaves().FirstOrDefault(m => m.Id == info.Id);
                if (existing != null)
                {
                    existing.Enabled = cb.IsChecked == true;

                    if (pendingEdits.TryGetValue(cb, out var edit))
                    {
                        existing.Description = edit.Description;
                        existing.CccOperationType = edit.CccOpType;
                        existing.Quantity = edit.Quantity;
                        existing.ExpectedPrice = edit.Price;
                        existing.ExpectedHours = edit.BodyHours;
                        existing.BodyLaborCategory = edit.BodyLaborCategory;
                        existing.RefinishHours = edit.RefinishHours;
                        existing.Conditions = edit.Condition;
                        existing.GroupId = edit.GroupId;
                    }

                    // Apply tag edits (insurance companies / vehicle types)
                    if (pendingTagEdits != null && pendingTagEdits.TryGetValue(info.Id, out var tagEdit))
                    {
                        existing.InsuranceCompanies = tagEdit.InsuranceCompanies;
                        existing.VehicleTypes = tagEdit.VehicleTypes;
                    }

                    config.UpdateMustHave(existing);
                }
            }

            // 3. Add pending new operations
            foreach (var newMh in pendingAdds)
            {
                if (!pendingDeletes.Contains(newMh.Id))
                {
                    // Apply tag edits to new operations too
                    if (pendingTagEdits != null && pendingTagEdits.TryGetValue(newMh.Id, out var newTagEdit))
                    {
                        newMh.InsuranceCompanies = newTagEdit.InsuranceCompanies;
                        newMh.VehicleTypes = newTagEdit.VehicleTypes;
                    }
                    config.AddMustHave(newMh);
                }
            }
        }
    }
}
