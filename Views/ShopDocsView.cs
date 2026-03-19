#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using McStudDesktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Shop Documents View - Checklists, forms, labor rates, and shop documents
    /// Features sub-tabs for different document types
    /// </summary>
    public sealed class ShopDocsView : UserControl
    {
        private readonly ChecklistService _checklistService;
        private readonly CustomChecklistService _customChecklistService;
        private readonly ShopDocsLayoutService _layoutService;

        // Sub-tab navigation (now using dropdown)
        private ComboBox? _docTypeCombo;
        private Grid? _subTabContent;
        private string _selectedWidgetId = "";

        // Widget management panel
        private Grid? _managementOverlay;
        private StackPanel? _managementList;

        // Cached view instances (keyed by widget ID)
        private readonly Dictionary<string, UIElement> _viewCache = new();

        // My Docs
        private readonly ShopDocsSettingsService _settingsService = ShopDocsSettingsService.Instance;
        private StackPanel? _myDocsListPanel;
        private TextBlock? _userFolderText;

        // Checklists UI
        private StackPanel? _checklistContent;
        private TextBox? _roNumberBox;
        private Checklist? _currentChecklist;
        private bool _isCurrentChecklistCustom = false;

        // Track checked items (key = "sectionIndex_itemIndex")
        private HashSet<string> _checkedItems = new();

        // Checklist tabs (Templates vs My Checklists)
        private Button? _templatesTabBtn;
        private Button? _myChecklistsTabBtn;
        private ComboBox? _checklistCombo;
        private bool _showingCustomChecklists = false;
        private Grid? _checklistEditorContainer;
        private ChecklistEditorView? _checklistEditor;
        private Grid? _checklistMainContainer;

        // Invoices (used within BuildInvoicesContentInner)
        private ColorTintInvoiceView? _colorTintInvoiceView;
        private ShopStockInvoiceView? _shopStockInvoiceView;

        // Vehicle Protection (PPF) customization
        private PPFPricingView? _ppfPricingView;
        private Grid? _ppfMainContainer;
        private Grid? _ppfEditorContainer;
        private PPFCustomizationView? _ppfCustomizationEditor;

        // Price Catalogs
        private PriceCatalogManagementView? _priceCatalogView;

        // Colors
        private static readonly Color DarkBg = Color.FromArgb(255, 18, 18, 18);
        private static readonly Color CardBg = Color.FromArgb(255, 28, 28, 28);
        private static readonly Color SectionBg = Color.FromArgb(255, 35, 35, 35);
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentGreen = Color.FromArgb(255, 0, 150, 80);
        private static readonly Color AccentOrange = Color.FromArgb(255, 255, 150, 0);
        private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

        public ShopDocsView()
        {
            _checklistService = ChecklistService.Instance;
            _customChecklistService = CustomChecklistService.Instance;
            _layoutService = ShopDocsLayoutService.Instance;
            _customChecklistService.ChecklistsChanged += (s, e) => RefreshChecklistDropdown();
            _layoutService.LayoutChanged += (s, e) =>
            {
                McstudDesktop.App.MainDispatcherQueue?.TryEnqueue(() => RebuildContent());
            };
            BuildUI();
        }

        private void BuildUI()
        {
            var rootBorder = new Border
            {
                Background = new SolidColorBrush(DarkBg)
            };

            // Use Grid for proper layout with scrolling
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header with dropdown
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // Header with dropdown selector
            var header = BuildHeaderWithDropdown();
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Content area (switches based on dropdown)
            _subTabContent = new Grid
            {
                Margin = new Thickness(12, 0, 12, 12)
            };

            // Build content from layout config
            BuildAllWidgetContent();

            Grid.SetRow(_subTabContent, 1);
            mainGrid.Children.Add(_subTabContent);

            rootBorder.Child = mainGrid;
            Content = rootBorder;

            // Select first item
            var visible = _layoutService.GetVisibleWidgets();
            if (visible.Count > 0)
                SelectWidget(visible[0].Id);
        }

        private void RebuildContent()
        {
            // Rebuild dropdown
            PopulateDropdown();

            // Rebuild content area
            if (_subTabContent != null)
            {
                _subTabContent.Children.Clear();
                BuildAllWidgetContent();
            }

            // Select first visible widget
            var visible = _layoutService.GetVisibleWidgets();
            if (visible.Count > 0)
                SelectWidget(visible[0].Id);
        }

        private void BuildAllWidgetContent()
        {
            var visibleWidgets = _layoutService.GetVisibleWidgets();
            for (int i = 0; i < visibleWidgets.Count; i++)
            {
                var widget = visibleWidgets[i];
                var content = BuildWidgetContent(widget, i);
                if (content != null)
                {
                    _subTabContent?.Children.Add(content);
                }
            }
        }

        private UIElement? BuildWidgetContent(WidgetEntry widget, int index)
        {
            switch (widget.WidgetType)
            {
                case WidgetType.Checklists:
                    return BuildChecklistsContentInner(widget.Id, index);
                case WidgetType.LaborRates:
                    return BuildLaborRatesContentInner(widget.Id, index);
                case WidgetType.TowBill:
                    return BuildTowBillContentInner(widget.Id, index);
                case WidgetType.Invoices:
                    return BuildInvoicesContentInner(widget.Id, index);
                case WidgetType.PPFPricing:
                    return BuildPPFContentInner(widget.Id, index);
                case WidgetType.PriceCatalogs:
                    return BuildPriceCatalogsContentInner(widget.Id, index);
                case WidgetType.MyDocs:
                    return BuildMyDocsContentInner(widget.Id, index);
                case WidgetType.TemplateForm:
                    return BuildTemplateFormContent(widget, index);
                default:
                    return null;
            }
        }

        private Border BuildHeaderWithDropdown()
        {
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(CardBg),
                Padding = new Thickness(12, 12, 12, 12),
                Margin = new Thickness(12, 12, 12, 0)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Title
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Spacer
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Dropdown
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Gear
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Help

            // Icon
            var icon = new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 22,
                Foreground = new SolidColorBrush(AccentBlue),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);
            headerGrid.Children.Add(icon);

            // Title
            var titleStack = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Shop Docs",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Fillable forms, checklists & documents",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            // Dropdown selector
            _docTypeCombo = new ComboBox
            {
                MinWidth = 220,
                MaxWidth = 300,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            PopulateDropdown();

            _docTypeCombo.SelectionChanged += (s, e) =>
            {
                if (_docTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string widgetId)
                {
                    SelectWidget(widgetId);
                }
            };

            Grid.SetColumn(_docTypeCombo, 3);
            headerGrid.Children.Add(_docTypeCombo);

            // Gear button for widget management
            var gearButton = new Button
            {
                Content = new FontIcon
                {
                    Glyph = "\uE713",
                    FontSize = 16
                },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(gearButton, "Customize widgets");
            gearButton.Click += OnGearButtonClick;
            Grid.SetColumn(gearButton, 4);
            headerGrid.Children.Add(gearButton);

            // Help button
            var helpButton = ContextualHelpButton.Create("shop-docs-tab");
            helpButton.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(helpButton, 5);
            headerGrid.Children.Add(helpButton);

            headerBorder.Child = headerGrid;
            return headerBorder;
        }

        private void PopulateDropdown()
        {
            if (_docTypeCombo == null) return;
            _docTypeCombo.SelectionChanged -= OnDropdownSelectionChanged;
            _docTypeCombo.Items.Clear();

            var visibleWidgets = _layoutService.GetVisibleWidgets();
            foreach (var widget in visibleWidgets)
            {
                var itemStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10
                };

                itemStack.Children.Add(new FontIcon
                {
                    Glyph = widget.Icon,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(AccentBlue)
                });

                var textStack = new StackPanel();
                textStack.Children.Add(new TextBlock
                {
                    Text = widget.Title,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = widget.Description,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextGray)
                });
                itemStack.Children.Add(textStack);

                _docTypeCombo.Items.Add(new ComboBoxItem
                {
                    Content = itemStack,
                    Tag = widget.Id
                });
            }

            if (_docTypeCombo.Items.Count > 0)
                _docTypeCombo.SelectedIndex = 0;

            _docTypeCombo.SelectionChanged += OnDropdownSelectionChanged;
        }

        private void OnDropdownSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_docTypeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string widgetId)
            {
                SelectWidget(widgetId);
            }
        }

        private void SelectWidget(string widgetId)
        {
            _selectedWidgetId = widgetId;

            // Update dropdown if needed (when called programmatically)
            if (_docTypeCombo != null)
            {
                for (int i = 0; i < _docTypeCombo.Items.Count; i++)
                {
                    if (_docTypeCombo.Items[i] is ComboBoxItem item && item.Tag is string id && id == widgetId)
                    {
                        if (_docTypeCombo.SelectedIndex != i)
                        {
                            _docTypeCombo.SelectionChanged -= OnDropdownSelectionChanged;
                            _docTypeCombo.SelectedIndex = i;
                            _docTypeCombo.SelectionChanged += OnDropdownSelectionChanged;
                        }
                        break;
                    }
                }
            }

            // Show/hide content
            if (_subTabContent == null) return;

            foreach (UIElement child in _subTabContent.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is string tag)
                {
                    fe.Visibility = tag == widgetId ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // Refresh My Docs list when switching to that widget
            var widget = _layoutService.GetAllWidgets().FirstOrDefault(w => w.Id == widgetId);
            if (widget?.WidgetType == WidgetType.MyDocs)
            {
                RefreshMyDocsList();
            }
        }

        #region Widget Management Panel

        private void OnGearButtonClick(object sender, RoutedEventArgs e)
        {
            if (_managementOverlay != null && _managementOverlay.Visibility == Visibility.Visible)
            {
                CloseManagementPanel();
                return;
            }
            ShowManagementPanel();
        }

        private void ShowManagementPanel()
        {
            if (_subTabContent == null) return;

            if (_managementOverlay != null)
                _subTabContent.Children.Remove(_managementOverlay);

            _managementOverlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 18, 18, 18)),
                Tag = "__management__"
            };

            var panel = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(40, 20, 40, 20),
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Title row
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            titleRow.Children.Add(new TextBlock
            {
                Text = "Customize Widgets",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var closeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 14 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6, 4, 6, 4)
            };
            closeBtn.Click += (s, e2) => CloseManagementPanel();
            Grid.SetColumn(closeBtn, 1);
            titleRow.Children.Add(closeBtn);

            mainStack.Children.Add(titleRow);

            mainStack.Children.Add(new TextBlock
            {
                Text = "Show, hide, and reorder your Shop Docs widgets",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray)
            });

            // Widget list
            _managementList = new StackPanel { Spacing = 4 };
            RefreshManagementList();
            mainStack.Children.Add(_managementList);

            // Action buttons
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var addBtn = new Button
            {
                Content = "+ Add Widget",
                Background = new SolidColorBrush(AccentGreen),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };
            addBtn.Click += OnAddWidgetClick;
            actionRow.Children.Add(addBtn);

            var resetBtn = new Button
            {
                Content = "Reset to Defaults",
                Background = new SolidColorBrush(Color.FromArgb(255, 120, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };
            resetBtn.Click += OnResetWidgetsClick;
            actionRow.Children.Add(resetBtn);

            var doneBtn = new Button
            {
                Content = "Done",
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 6, 16, 6)
            };
            doneBtn.Click += (s, e2) => CloseManagementPanel();
            actionRow.Children.Add(doneBtn);

            mainStack.Children.Add(actionRow);

            scroll.Content = mainStack;
            panel.Child = scroll;
            _managementOverlay.Children.Add(panel);

            _managementOverlay.Tapped += (s, e2) =>
            {
                if (e2.OriginalSource == _managementOverlay)
                    CloseManagementPanel();
            };

            _subTabContent.Children.Add(_managementOverlay);
        }

        private void CloseManagementPanel()
        {
            if (_managementOverlay != null && _subTabContent != null)
            {
                _subTabContent.Children.Remove(_managementOverlay);
                _managementOverlay = null;
                _managementList = null;
            }
        }

        private void RefreshManagementList()
        {
            if (_managementList == null) return;
            _managementList.Children.Clear();

            var allWidgets = _layoutService.GetAllWidgets();
            for (int i = 0; i < allWidgets.Count; i++)
            {
                _managementList.Children.Add(BuildManagementRow(allWidgets[i], i, allWidgets.Count));
            }
        }

        private Border BuildManagementRow(WidgetEntry widget, int index, int total)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(widget.IsVisible
                    ? Color.FromArgb(255, 35, 35, 35)
                    : Color.FromArgb(255, 25, 25, 25)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Eye
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Up
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Down
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Delete

            // Eye toggle
            var wId = widget.Id;
            var wVisible = widget.IsVisible;
            var eyeBtn = new Button
            {
                Content = new FontIcon
                {
                    Glyph = wVisible ? "\uE7B3" : "\uED1A",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(wVisible ? Colors.White : TextGray)
                },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            ToolTipService.SetToolTip(eyeBtn, wVisible ? "Hide" : "Show");
            eyeBtn.Click += (s, e2) =>
            {
                _layoutService.SetWidgetVisibility(wId, !wVisible);
                RefreshManagementList();
            };
            Grid.SetColumn(eyeBtn, 0);
            grid.Children.Add(eyeBtn);

            // Widget icon
            var iconElement = new FontIcon
            {
                Glyph = widget.Icon,
                FontSize = 14,
                Foreground = new SolidColorBrush(wVisible ? AccentBlue : TextGray),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconElement, 1);
            grid.Children.Add(iconElement);

            // Title
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = widget.Title,
                FontSize = 13,
                Foreground = new SolidColorBrush(wVisible ? Colors.White : TextGray)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = widget.IsBuiltIn ? "Built-in" : "Custom widget",
                FontSize = 9,
                Foreground = new SolidColorBrush(widget.IsBuiltIn ? TextGray : AccentOrange)
            });
            Grid.SetColumn(titleStack, 2);
            grid.Children.Add(titleStack);

            // Move up
            var upBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE70E", FontSize = 12 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6, 4, 6, 4),
                IsEnabled = index > 0
            };
            upBtn.Click += (s, e2) =>
            {
                _layoutService.MoveWidget(wId, -1);
                RefreshManagementList();
            };
            Grid.SetColumn(upBtn, 3);
            grid.Children.Add(upBtn);

            // Move down
            var downBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 },
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(6, 4, 6, 4),
                IsEnabled = index < total - 1
            };
            downBtn.Click += (s, e2) =>
            {
                _layoutService.MoveWidget(wId, 1);
                RefreshManagementList();
            };
            Grid.SetColumn(downBtn, 4);
            grid.Children.Add(downBtn);

            // Delete (user widgets only)
            if (!widget.IsBuiltIn)
            {
                var deleteBtn = new Button
                {
                    Content = new FontIcon
                    {
                        Glyph = "\uE74D",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 80, 80))
                    },
                    Background = new SolidColorBrush(Colors.Transparent),
                    Padding = new Thickness(6, 4, 6, 4)
                };
                ToolTipService.SetToolTip(deleteBtn, "Delete widget");
                deleteBtn.Click += async (s, e2) =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Delete Widget",
                        Content = $"Delete '{widget.Title}'? This cannot be undone.",
                        PrimaryButtonText = "Delete",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.XamlRoot
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (widget.TemplateId != null)
                        {
                            var template = ShopDocTemplateService.Instance.GetTemplate(ShopDocType.Custom, widget.TemplateId);
                            if (template != null)
                                ShopDocTemplateService.Instance.DeleteUserTemplate(template);
                        }
                        _viewCache.Remove(wId);
                        _layoutService.RemoveUserWidget(wId);
                        RefreshManagementList();
                    }
                };
                Grid.SetColumn(deleteBtn, 5);
                grid.Children.Add(deleteBtn);
            }

            border.Child = grid;
            return border;
        }

        private async void OnAddWidgetClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Add Widget",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var formStack = new StackPanel { Spacing = 12 };

            formStack.Children.Add(new TextBlock
            {
                Text = "Widget Name",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var nameBox = new TextBox
            {
                PlaceholderText = "e.g., Supplement Form",
                Width = 300
            };
            formStack.Children.Add(nameBox);

            formStack.Children.Add(new TextBlock
            {
                Text = "Start from",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 4, 0, 0)
            });

            var typeCombo = new ComboBox { Width = 300 };
            typeCombo.Items.Add(new ComboBoxItem { Content = "Blank form", Tag = "blank" });
            typeCombo.Items.Add(new ComboBoxItem { Content = "Copy existing template...", Tag = "copy" });
            typeCombo.SelectedIndex = 0;
            formStack.Children.Add(typeCombo);

            var templateCombo = new ComboBox
            {
                Width = 300,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var templateService = ShopDocTemplateService.Instance;
            foreach (ShopDocType docType in Enum.GetValues<ShopDocType>())
            {
                foreach (var t in templateService.GetTemplates(docType))
                {
                    templateCombo.Items.Add(new ComboBoxItem
                    {
                        Content = $"{t.Name} ({docType})",
                        Tag = t
                    });
                }
            }
            if (templateCombo.Items.Count > 0)
                templateCombo.SelectedIndex = 0;
            formStack.Children.Add(templateCombo);

            typeCombo.SelectionChanged += (s2, e2) =>
            {
                if (typeCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
                    templateCombo.Visibility = tag == "copy" ? Visibility.Visible : Visibility.Collapsed;
            };

            dialog.Content = formStack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                var name = nameBox.Text.Trim();
                string? templateId = null;

                if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string typeTag)
                {
                    if (typeTag == "blank")
                    {
                        var template = templateService.CreateBlankTemplate(name);
                        templateId = template.Id;
                    }
                    else if (typeTag == "copy" && templateCombo.SelectedItem is ComboBoxItem tItem && tItem.Tag is ShopDocTemplate source)
                    {
                        var copy = templateService.MakeCopyAsCustom(source, name);
                        templateId = copy.Id;
                    }
                }

                _layoutService.AddUserWidget(name, "\uE8A5", templateId);
                RefreshManagementList();
            }
        }

        private async void OnResetWidgetsClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Reset Widgets",
                Content = "Reset to the default 7 built-in widgets? Custom widgets will be removed from the layout.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _viewCache.Clear();
                _layoutService.ResetToDefaults();
                RefreshManagementList();
            }
        }

        #endregion

        private UIElement BuildChecklistsContentInner(string widgetId, int index)
        {
            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            // Main checklist view
            _checklistMainContainer = new Grid { Visibility = Visibility.Visible };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var stack = new StackPanel { Spacing = 12 };

            // Checklist selector with tabs
            stack.Children.Add(BuildChecklistSelector());

            // Checklist content
            _checklistContent = new StackPanel { Spacing = 8 };
            stack.Children.Add(_checklistContent);

            // Load first checklist
            var checklists = _checklistService.GetChecklists();
            if (checklists.Count > 0)
            {
                LoadChecklist(checklists[0]);
            }

            scrollViewer.Content = stack;
            _checklistMainContainer.Children.Add(scrollViewer);
            container.Children.Add(_checklistMainContainer);

            // Editor container (hidden initially)
            _checklistEditorContainer = new Grid { Visibility = Visibility.Collapsed };
            _checklistEditor = new ChecklistEditorView();
            _checklistEditor.SaveRequested += OnEditorSave;
            _checklistEditor.CloseRequested += OnEditorClose;
            _checklistEditorContainer.Children.Add(_checklistEditor);
            container.Children.Add(_checklistEditorContainer);

            return container;
        }

        private UIElement BuildLaborRatesContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            var view = new LaborRatesView();
            container.Children.Add(view);

            _viewCache[widgetId] = container;
            return container;
        }

        private UIElement BuildTowBillContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            var view = new TowBillView();
            container.Children.Add(view);

            _viewCache[widgetId] = container;
            return container;
        }

        private UIElement BuildInvoicesContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sub-tabs
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

            // Invoice sub-tabs (Color Tint vs Shop Stock)
            var subTabPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var colorTintBtn = new Button
            {
                Content = "Color Tint Invoice",
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };

            var shopStockBtn = new Button
            {
                Content = "Shop Stock Invoice",
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                Foreground = new SolidColorBrush(Colors.White),
                Padding = new Thickness(12, 6, 12, 6)
            };

            colorTintBtn.Click += (s, e) =>
            {
                colorTintBtn.Background = new SolidColorBrush(AccentBlue);
                shopStockBtn.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
                if (_colorTintInvoiceView != null) _colorTintInvoiceView.Visibility = Visibility.Visible;
                if (_shopStockInvoiceView != null) _shopStockInvoiceView.Visibility = Visibility.Collapsed;
            };

            shopStockBtn.Click += (s, e) =>
            {
                shopStockBtn.Background = new SolidColorBrush(AccentBlue);
                colorTintBtn.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
                if (_colorTintInvoiceView != null) _colorTintInvoiceView.Visibility = Visibility.Collapsed;
                if (_shopStockInvoiceView != null) _shopStockInvoiceView.Visibility = Visibility.Visible;
            };

            subTabPanel.Children.Add(colorTintBtn);
            subTabPanel.Children.Add(shopStockBtn);
            Grid.SetRow(subTabPanel, 0);
            container.Children.Add(subTabPanel);

            // Invoice content
            var invoiceSubContent = new Grid();
            Grid.SetRow(invoiceSubContent, 1);

            _colorTintInvoiceView = new ColorTintInvoiceView { Visibility = Visibility.Visible };
            _shopStockInvoiceView = new ShopStockInvoiceView { Visibility = Visibility.Collapsed };

            invoiceSubContent.Children.Add(_colorTintInvoiceView);
            invoiceSubContent.Children.Add(_shopStockInvoiceView);
            container.Children.Add(invoiceSubContent);

            _viewCache[widgetId] = container;
            return container;
        }

        private UIElement BuildPPFContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            // Main container (shown by default)
            _ppfMainContainer = new Grid();
            _ppfPricingView = new PPFPricingView();
            _ppfPricingView.CustomizeRequested += (_, _) =>
            {
                _ppfMainContainer!.Visibility = Visibility.Collapsed;
                _ppfEditorContainer!.Visibility = Visibility.Visible;
                _ppfCustomizationEditor?.LoadData();
            };
            _ppfMainContainer.Children.Add(_ppfPricingView);
            container.Children.Add(_ppfMainContainer);

            // Editor container (hidden initially)
            _ppfEditorContainer = new Grid { Visibility = Visibility.Collapsed };
            _ppfCustomizationEditor = new PPFCustomizationView();
            _ppfCustomizationEditor.SaveRequested += (_, _) =>
            {
                _ppfEditorContainer!.Visibility = Visibility.Collapsed;
                _ppfMainContainer!.Visibility = Visibility.Visible;
                _ppfPricingView?.RefreshAfterCustomization();
            };
            _ppfCustomizationEditor.CloseRequested += (_, _) =>
            {
                _ppfEditorContainer!.Visibility = Visibility.Collapsed;
                _ppfMainContainer!.Visibility = Visibility.Visible;
            };
            _ppfEditorContainer.Children.Add(_ppfCustomizationEditor);
            container.Children.Add(_ppfEditorContainer);

            _viewCache[widgetId] = container;
            return container;
        }

        private UIElement BuildPriceCatalogsContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            _priceCatalogView = new PriceCatalogManagementView();
            container.Children.Add(_priceCatalogView);

            _viewCache[widgetId] = container;
            return container;
        }

        private UIElement BuildTemplateFormContent(WidgetEntry widget, int index)
        {
            if (_viewCache.TryGetValue(widget.Id, out var cached))
                return cached;

            var container = new Grid
            {
                Tag = widget.Id,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            var formBuilder = new TemplateFormBuilder(ShopDocType.Custom);
            container.Children.Add(formBuilder);

            _viewCache[widget.Id] = container;
            return container;
        }

        private UIElement BuildMyDocsContentInner(string widgetId, int index)
        {
            if (_viewCache.TryGetValue(widgetId, out var cached))
                return cached;

            var scrollViewer = new ScrollViewer
            {
                Tag = widgetId,
                Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var mainStack = new StackPanel { Spacing = 15, Padding = new Thickness(0, 10, 0, 10) };

            // Header
            mainStack.Children.Add(new TextBlock
            {
                Text = "Manage your custom documents and templates",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(0, 0, 0, 5)
            });

            // Folder Settings Section
            var (folderWrapper, folderContent) = CreateMyDocsSection("Document Folders", "\uE8B7");

            // Base folder (read-only)
            var baseFolderRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            baseFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            baseFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            baseFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            baseFolderRow.Children.Add(new TextBlock
            {
                Text = "App Folder:",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            });

            var baseFolderText = new TextBlock
            {
                Text = _settingsService.GetBaseDocsFolder(),
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(baseFolderText, 1);
            baseFolderRow.Children.Add(baseFolderText);

            var openBaseBtn = new Button
            {
                Content = "\uE8DA",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Padding = new Thickness(8, 4, 8, 4)
            };
            ToolTipService.SetToolTip(openBaseBtn, "Open folder");
            openBaseBtn.Click += (s, e) => _settingsService.OpenFolder(_settingsService.GetBaseDocsFolder());
            Grid.SetColumn(openBaseBtn, 2);
            baseFolderRow.Children.Add(openBaseBtn);

            folderContent.Children.Add(baseFolderRow);

            // User folder
            var userFolderRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            userFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            userFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            userFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            userFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            userFolderRow.Children.Add(new TextBlock
            {
                Text = "My Docs:",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            });

            _userFolderText = new TextBlock
            {
                Text = _settingsService.GetUserDocsFolder(),
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_userFolderText, 1);
            userFolderRow.Children.Add(_userFolderText);

            var changeBtn = new Button
            {
                Content = "Change...",
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            changeBtn.Click += OnChangeFolderClick;
            Grid.SetColumn(changeBtn, 2);
            userFolderRow.Children.Add(changeBtn);

            var openUserBtn = new Button
            {
                Content = "\uE8DA",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0)
            };
            openUserBtn.Click += (s, e) => _settingsService.OpenFolder(_settingsService.GetUserDocsFolder());
            Grid.SetColumn(openUserBtn, 3);
            userFolderRow.Children.Add(openUserBtn);

            folderContent.Children.Add(userFolderRow);

            mainStack.Children.Add(folderWrapper);

            // My Documents Section
            var (docsWrapper, docsContent) = CreateMyDocsSection("My Documents", "\uE7C3");

            // Action buttons
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 8)
            };

            var importBtn = new Button
            {
                Content = "\uE8E5  Import Document",
                FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI"),
                Padding = new Thickness(12, 6, 12, 6)
            };
            importBtn.Click += OnImportDocumentClick;
            actionRow.Children.Add(importBtn);

            var refreshBtn = new Button
            {
                Content = "\uE72C  Refresh",
                FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI"),
                Padding = new Thickness(12, 6, 12, 6)
            };
            refreshBtn.Click += (s, e) => RefreshMyDocsList();
            actionRow.Children.Add(refreshBtn);

            docsContent.Children.Add(actionRow);

            // Documents list
            _myDocsListPanel = new StackPanel { Spacing = 4 };
            docsContent.Children.Add(_myDocsListPanel);

            mainStack.Children.Add(docsWrapper);

            // Instructions
            var (instructionsWrapper, instructionsContent) = CreateMyDocsSection("How to Add Documents", "\uE897");
            var instructionsList = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };

            var instructions = new[]
            {
                "1. Click 'Change...' to set your custom documents folder",
                "2. Use 'Import Document' to copy files to your docs folder",
                "3. Or manually copy files to: Documents\\McStudDocs",
                "4. Create subfolders to organize (Checklists, Forms, etc.)",
                "5. JSON checklists placed in Checklists subfolder will appear in the Checklists tab"
            };

            foreach (var instruction in instructions)
            {
                instructionsList.Children.Add(new TextBlock
                {
                    Text = instruction,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextGray)
                });
            }

            instructionsContent.Children.Add(instructionsList);
            mainStack.Children.Add(instructionsWrapper);

            scrollViewer.Content = mainStack;
            _viewCache[widgetId] = scrollViewer;
            return scrollViewer;
        }

        private (Border wrapper, StackPanel content) CreateMyDocsSection(string title, string icon)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var content = new StackPanel();

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 4)
            };

            header.Children.Add(new FontIcon
            {
                Glyph = icon,
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentBlue)
            });

            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            content.Children.Add(header);
            border.Child = content;

            return (border, content);
        }

        private void RefreshMyDocsList()
        {
            if (_myDocsListPanel == null) return;

            _myDocsListPanel.Children.Clear();

            var docs = _settingsService.GetUserDocuments();

            if (docs.Count == 0)
            {
                _myDocsListPanel.Children.Add(new TextBlock
                {
                    Text = "No documents found. Import documents or add files to your docs folder.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextGray),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                return;
            }

            // Group by folder
            var grouped = docs.GroupBy(d => Path.GetDirectoryName(d.RelativePath) ?? "").OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    _myDocsListPanel.Children.Add(new TextBlock
                    {
                        Text = group.Key,
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(AccentBlue),
                        Margin = new Thickness(0, 8, 0, 4)
                    });
                }

                foreach (var doc in group)
                {
                    _myDocsListPanel.Children.Add(CreateDocumentRow(doc));
                }
            }
        }

        private Border CreateDocumentRow(DocumentInfo doc)
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon based on type
            var iconGlyph = doc.DocumentType switch
            {
                "PDF" => "\uEA90",
                "Word" => "\uE8A5",
                "Excel" => "\uE9F9",
                "JSON" => "\uE943",
                _ => "\uE7C3"
            };

            grid.Children.Add(new FontIcon
            {
                Glyph = iconGlyph,
                FontSize = 14,
                Foreground = new SolidColorBrush(AccentBlue),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = doc.DisplayName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });
            textStack.Children.Add(new TextBlock
            {
                Text = doc.DocumentType,
                FontSize = 9,
                Foreground = new SolidColorBrush(TextGray)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Open button
            var openBtn = new Button
            {
                Content = "\uE8DA",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(4, 0, 0, 0)
            };
            ToolTipService.SetToolTip(openBtn, "Open");
            openBtn.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = doc.FilePath,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            Grid.SetColumn(openBtn, 2);
            grid.Children.Add(openBtn);

            // Delete button (only for non-base docs)
            if (!doc.IsBase)
            {
                var deleteBtn = new Button
                {
                    Content = "\uE74D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                ToolTipService.SetToolTip(deleteBtn, "Delete");
                deleteBtn.Click += async (s, e) =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Delete Document",
                        Content = $"Delete '{doc.DisplayName}'?",
                        PrimaryButtonText = "Delete",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.XamlRoot
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (_settingsService.DeleteUserDocument(doc))
                        {
                            RefreshMyDocsList();
                        }
                    }
                };
                Grid.SetColumn(deleteBtn, 3);
                grid.Children.Add(deleteBtn);
            }

            border.Child = grid;

            // Double-click to open
            border.DoubleTapped += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = doc.FilePath,
                        UseShellExecute = true
                    });
                }
                catch { }
            };

            return border;
        }

        private async void OnChangeFolderClick(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            // Get the window handle for the picker
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                var settings = _settingsService.GetSettings();
                settings.CustomDocsFolder = folder.Path;
                settings.UseCustomFolder = true;
                _settingsService.SaveSettings(settings);

                if (_userFolderText != null)
                {
                    _userFolderText.Text = folder.Path;
                }

                RefreshMyDocsList();
            }
        }

        private async void OnImportDocumentClick(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".json");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    // Determine subfolder based on file type
                    string? subFolder = null;
                    if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        subFolder = "Checklists";
                    }

                    _settingsService.ImportDocument(file.Path, subFolder);
                }

                RefreshMyDocsList();
            }
        }

        private Border BuildChecklistSelector()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };

            var mainStack = new StackPanel { Spacing = 10 };

            // Row 1: Tabs (Templates vs My Checklists)
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };

            _templatesTabBtn = new Button
            {
                Content = "📋 Templates",
                Padding = new Thickness(15, 6, 15, 6),
                Background = new SolidColorBrush(AccentBlue),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12
            };
            _templatesTabBtn.Click += (s, e) => SwitchChecklistTab(false);
            tabRow.Children.Add(_templatesTabBtn);

            _myChecklistsTabBtn = new Button
            {
                Content = "⭐ My Checklists",
                Padding = new Thickness(15, 6, 15, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12
            };
            _myChecklistsTabBtn.Click += (s, e) => SwitchChecklistTab(true);
            tabRow.Children.Add(_myChecklistsTabBtn);

            // Create New button
            var createNewBtn = new Button
            {
                Content = "+ Create New",
                Padding = new Thickness(15, 6, 15, 6),
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 80)),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                Margin = new Thickness(15, 0, 0, 0)
            };
            createNewBtn.Click += OnCreateNewChecklist;
            tabRow.Children.Add(createNewBtn);

            mainStack.Children.Add(tabRow);

            // Row 2: Checklist selector and actions
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Dropdown
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // RO
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Duplicate
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Edit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Copy
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // PDF

            // Checklist dropdown
            _checklistCombo = new ComboBox
            {
                Width = 250,
                FontSize = 12,
                Background = new SolidColorBrush(SectionBg)
            };
            _checklistCombo.SelectionChanged += OnChecklistSelectionChanged;
            Grid.SetColumn(_checklistCombo, 0);
            grid.Children.Add(_checklistCombo);

            // RO Number input
            var roStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(15, 0, 0, 0)
            };

            roStack.Children.Add(new TextBlock
            {
                Text = "RO #:",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            });

            _roNumberBox = new TextBox
            {
                Width = 100,
                FontSize = 12,
                PlaceholderText = "Optional",
                Background = new SolidColorBrush(SectionBg)
            };
            roStack.Children.Add(_roNumberBox);

            // RO line length preview/editor
            var roLineLabel = new TextBlock
            {
                Text = "Line:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            roStack.Children.Add(roLineLabel);

            var roLinePreview = new TextBlock
            {
                Text = new string('_', _checklistService.RoLineLength),
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            };

            var roLineBox = new NumberBox
            {
                Value = _checklistService.RoLineLength,
                Minimum = 5,
                Maximum = 60,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 90,
                FontSize = 11,
                SmallChange = 5,
                LargeChange = 10
            };
            roLineBox.ValueChanged += (s, args) =>
            {
                if (!double.IsNaN(args.NewValue))
                {
                    var len = (int)args.NewValue;
                    _checklistService.RoLineLength = len;
                    roLinePreview.Text = new string('_', len);
                }
            };
            roStack.Children.Add(roLineBox);
            roStack.Children.Add(roLinePreview);

            Grid.SetColumn(roStack, 1);
            grid.Children.Add(roStack);

            // Duplicate button
            var duplicateButton = CreateActionButton("Duplicate", "\uE8C8", Color.FromArgb(255, 150, 100, 0));
            duplicateButton.Click += OnDuplicateChecklist;
            duplicateButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(duplicateButton, 2);
            grid.Children.Add(duplicateButton);

            // Edit button (only visible for custom checklists)
            var editButton = CreateActionButton("Edit", "\uE70F", Color.FromArgb(255, 100, 100, 180));
            editButton.Click += OnEditChecklist;
            editButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(editButton, 3);
            grid.Children.Add(editButton);

            // Copy button
            var copyButton = CreateActionButton("Copy", "\uE8C8", AccentBlue);
            copyButton.Click += OnCopyClick;
            copyButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(copyButton, 4);
            grid.Children.Add(copyButton);

            // Export to PDF button
            var printButton = CreateActionButton("Export to PDF", "\uE749", AccentGreen);
            printButton.Click += OnPrintClick;
            printButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(printButton, 5);
            grid.Children.Add(printButton);

            mainStack.Children.Add(grid);

            // Populate dropdown with templates
            RefreshChecklistDropdown();

            border.Child = mainStack;
            return border;
        }

        private void SwitchChecklistTab(bool showCustom)
        {
            _showingCustomChecklists = showCustom;

            // Update tab button styles
            if (_templatesTabBtn != null)
            {
                _templatesTabBtn.Background = new SolidColorBrush(
                    showCustom ? Color.FromArgb(255, 50, 50, 50) : AccentBlue);
            }
            if (_myChecklistsTabBtn != null)
            {
                _myChecklistsTabBtn.Background = new SolidColorBrush(
                    showCustom ? AccentBlue : Color.FromArgb(255, 50, 50, 50));
            }

            RefreshChecklistDropdown();
        }

        private void RefreshChecklistDropdown()
        {
            if (_checklistCombo == null) return;

            _checklistCombo.Items.Clear();

            if (_showingCustomChecklists)
            {
                var customChecklists = _customChecklistService.GetCustomChecklists();
                foreach (var checklist in customChecklists)
                {
                    _checklistCombo.Items.Add(new ComboBoxItem
                    {
                        Content = $"⭐ {checklist.Title}",
                        Tag = checklist.Id
                    });
                }

                if (_checklistCombo.Items.Count == 0)
                {
                    _checklistCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "(No custom checklists yet)",
                        Tag = null,
                        IsEnabled = false
                    });
                }
            }
            else
            {
                var checklists = _checklistService.GetChecklists();
                foreach (var checklist in checklists)
                {
                    _checklistCombo.Items.Add(new ComboBoxItem
                    {
                        Content = checklist.Title,
                        Tag = checklist.Id
                    });
                }
            }

            if (_checklistCombo.Items.Count > 0)
            {
                _checklistCombo.SelectedIndex = 0;
            }
        }

        private void OnChecklistSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_checklistCombo?.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                Checklist? checklist;
                if (_showingCustomChecklists)
                {
                    checklist = _customChecklistService.GetChecklist(id);
                    _isCurrentChecklistCustom = true;
                }
                else
                {
                    checklist = _checklistService.GetChecklist(id);
                    _isCurrentChecklistCustom = false;
                }

                if (checklist != null)
                {
                    LoadChecklist(checklist);
                }
            }
        }

        private async void OnCreateNewChecklist(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Create New Checklist",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var textBox = new TextBox
            {
                PlaceholderText = "Checklist name...",
                Width = 300
            };
            dialog.Content = textBox;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                var newChecklist = _customChecklistService.CreateNewChecklist(textBox.Text.Trim());
                SwitchChecklistTab(true); // Switch to My Checklists tab

                // Select the new checklist
                for (int i = 0; i < (_checklistCombo?.Items.Count ?? 0); i++)
                {
                    if (_checklistCombo?.Items[i] is ComboBoxItem item && item.Tag?.ToString() == newChecklist.Id)
                    {
                        _checklistCombo.SelectedIndex = i;
                        break;
                    }
                }

                // Open editor for the new checklist
                OpenEditor(newChecklist, true);
            }
        }

        private void OnDuplicateChecklist(object sender, RoutedEventArgs e)
        {
            if (_currentChecklist == null) return;

            var duplicated = _customChecklistService.DuplicateChecklist(_currentChecklist);
            SwitchChecklistTab(true); // Switch to My Checklists

            // Select the duplicated checklist
            for (int i = 0; i < (_checklistCombo?.Items.Count ?? 0); i++)
            {
                if (_checklistCombo?.Items[i] is ComboBoxItem item && item.Tag?.ToString() == duplicated.Id)
                {
                    _checklistCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        private void OnEditChecklist(object sender, RoutedEventArgs e)
        {
            if (_currentChecklist == null) return;

            if (!_isCurrentChecklistCustom)
            {
                // For templates, duplicate first then edit
                var duplicated = _customChecklistService.DuplicateChecklist(_currentChecklist, _currentChecklist.Title);
                OpenEditor(duplicated, true);
            }
            else
            {
                OpenEditor(_currentChecklist, true);
            }
        }

        private void OpenEditor(Checklist checklist, bool isCustom)
        {
            _checklistEditor?.LoadChecklist(checklist, isCustom);
            _checklistMainContainer!.Visibility = Visibility.Collapsed;
            _checklistEditorContainer!.Visibility = Visibility.Visible;
        }

        private void OnEditorSave(object? sender, EventArgs e)
        {
            CloseEditor();
            SwitchChecklistTab(true);
            RefreshChecklistDropdown();

            // Select the edited checklist
            var editedChecklist = _checklistEditor?.GetChecklist();
            if (editedChecklist != null)
            {
                for (int i = 0; i < (_checklistCombo?.Items.Count ?? 0); i++)
                {
                    if (_checklistCombo?.Items[i] is ComboBoxItem item && item.Tag?.ToString() == editedChecklist.Id)
                    {
                        _checklistCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void OnEditorClose(object? sender, EventArgs e)
        {
            CloseEditor();
        }

        private void CloseEditor()
        {
            _checklistMainContainer!.Visibility = Visibility.Visible;
            _checklistEditorContainer!.Visibility = Visibility.Collapsed;
        }

        private Button CreateActionButton(string text, string glyph, Color color)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            stack.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });

            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            });

            return new Button
            {
                Content = stack,
                Background = new SolidColorBrush(color),
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(4)
            };
        }

        private void LoadChecklist(Checklist checklist)
        {
            _currentChecklist = checklist;
            _checkedItems.Clear(); // Clear checked items when loading new checklist
            _sectionStatusIndicators.Clear();
            _checklistContent?.Children.Clear();

            if (_checklistContent == null || checklist.Sections == null) return;

            // Header card with stats
            var headerCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 160)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = checklist.Title ?? "Checklist",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            // Show shop name from settings (user-editable), fall back to checklist's shopName
            var shopName = ShopDocsSettingsService.Instance.GetSettings().ShopName;
            if (string.IsNullOrEmpty(shopName))
                shopName = checklist.ShopName;
            if (!string.IsNullOrEmpty(shopName))
            {
                titleStack.Children.Add(new TextBlock
                {
                    Text = shopName,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 210, 255)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            // Stats panel
            var totalItems = checklist.Sections.Sum(s => s.Items?.Count ?? 0);
            var requiredItems = checklist.Sections.Sum(s => s.Items?.Count(i => i.Required) ?? 0);
            var sectionCount = checklist.Sections.Count;

            var statsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16
            };

            statsPanel.Children.Add(CreateStatBox(sectionCount.ToString(), "Sections"));
            statsPanel.Children.Add(CreateStatBox(totalItems.ToString(), "Items"));
            statsPanel.Children.Add(CreateStatBox(requiredItems.ToString(), "Required"));

            Grid.SetColumn(statsPanel, 1);
            headerGrid.Children.Add(statsPanel);

            headerCard.Child = headerGrid;
            _checklistContent.Children.Add(headerCard);

            // Progress tracking bar
            var progressCard = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var progressStack = new StackPanel { Spacing = 6 };

            var progressHeader = new Grid();
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            progressHeader.Children.Add(new TextBlock
            {
                Text = "Progress",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            _progressText = new TextBlock
            {
                Text = $"0/{totalItems} checked (0/{requiredItems} required)",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(_progressText, 1);
            progressHeader.Children.Add(_progressText);

            progressStack.Children.Add(progressHeader);

            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = totalItems,
                Value = 0,
                Height = 8,
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(AccentGreen)
            };
            progressStack.Children.Add(_progressBar);

            // Clear All / Check All buttons
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var clearAllBtn = new Button
            {
                Content = "Clear All",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
            };
            clearAllBtn.Click += (s, e) =>
            {
                _checkedItems.Clear();
                LoadChecklist(_currentChecklist!); // Reload to update checkboxes
            };
            buttonRow.Children.Add(clearAllBtn);

            var checkAllBtn = new Button
            {
                Content = "Check All",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
            };
            checkAllBtn.Click += (s, e) =>
            {
                if (_currentChecklist?.Sections == null) return;
                for (int si = 0; si < _currentChecklist.Sections.Count; si++)
                {
                    var items = _currentChecklist.Sections[si].Items ?? new List<ChecklistItem>();
                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        _checkedItems.Add($"{si}_{ii}");
                    }
                }
                LoadChecklist(_currentChecklist!); // Reload to update checkboxes
            };
            buttonRow.Children.Add(checkAllBtn);

            var checkRequiredBtn = new Button
            {
                Content = "Check Required Only",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60))
            };
            checkRequiredBtn.Click += (s, e) =>
            {
                if (_currentChecklist?.Sections == null) return;
                _checkedItems.Clear();
                for (int si = 0; si < _currentChecklist.Sections.Count; si++)
                {
                    var items = _currentChecklist.Sections[si].Items ?? new List<ChecklistItem>();
                    for (int ii = 0; ii < items.Count; ii++)
                    {
                        if (items[ii].Required)
                            _checkedItems.Add($"{si}_{ii}");
                    }
                }
                LoadChecklist(_currentChecklist!); // Reload to update checkboxes
            };
            buttonRow.Children.Add(checkRequiredBtn);

            progressStack.Children.Add(buttonRow);

            progressCard.Child = progressStack;
            _checklistContent.Children.Add(progressCard);

            // Two-column layout for sections if more than 4
            if (checklist.Sections.Count > 4)
            {
                var columnsGrid = new Grid();
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var leftColumn = new StackPanel { Spacing = 8 };
                var rightColumn = new StackPanel { Spacing = 8 };

                var midPoint = (checklist.Sections.Count + 1) / 2;
                for (int i = 0; i < checklist.Sections.Count; i++)
                {
                    var sectionCard = BuildSectionCard(checklist.Sections[i], i);
                    if (i < midPoint)
                        leftColumn.Children.Add(sectionCard);
                    else
                        rightColumn.Children.Add(sectionCard);
                }

                Grid.SetColumn(leftColumn, 0);
                Grid.SetColumn(rightColumn, 2);
                columnsGrid.Children.Add(leftColumn);
                columnsGrid.Children.Add(rightColumn);

                _checklistContent.Children.Add(columnsGrid);
            }
            else
            {
                // Single column for fewer sections
                for (int i = 0; i < checklist.Sections.Count; i++)
                {
                    _checklistContent.Children.Add(BuildSectionCard(checklist.Sections[i], i));
                }
            }

            // Legend footer
            var legend = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var legendText = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray)
            };
            legendText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "* ", Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 80, 80)), FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            legendText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "= Required Item   |   Version " + (checklist.Version ?? "1.0") });
            legend.Child = legendText;
            _checklistContent.Children.Add(legend);
        }

        private StackPanel CreateStatBox(string value, string label)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 210, 255)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return stack;
        }

        private Border BuildSectionCard(ChecklistSection section, int sectionIndex)
        {
            // Alternate section header colors
            var headerColors = new[] {
                Color.FromArgb(255, 0, 100, 180),   // Blue
                Color.FromArgb(255, 0, 130, 90),    // Green
                Color.FromArgb(255, 140, 80, 0),    // Orange
                Color.FromArgb(255, 100, 60, 140),  // Purple
                Color.FromArgb(255, 150, 50, 50),   // Red
                Color.FromArgb(255, 50, 100, 120)   // Teal
            };
            var headerColor = headerColors[sectionIndex % headerColors.Length];

            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel();

            // Section header with color
            var header = new Border
            {
                Background = new SolidColorBrush(headerColor),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(5, 5, 0, 0)
            };

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            var iconGlyph = GetIconGlyph(section.Icon);
            headerStack.Children.Add(new FontIcon
            {
                Glyph = iconGlyph,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = section.Title ?? "Section",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            var itemCount = section.Items?.Count ?? 0;
            var reqCount = section.Items?.Count(i => i.Required) ?? 0;
            headerStack.Children.Add(new TextBlock
            {
                Text = reqCount > 0 ? $"({itemCount} items, {reqCount} req)" : $"({itemCount} items)",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            });

            header.Child = headerStack;
            mainStack.Children.Add(header);

            // Items container
            var itemsContainer = new StackPanel
            {
                Padding = new Thickness(8, 6, 8, 8),
                Spacing = 2
            };

            if (section.Items != null)
            {
                for (int i = 0; i < section.Items.Count; i++)
                {
                    itemsContainer.Children.Add(BuildChecklistItem(section.Items[i], i % 2 == 1, sectionIndex, i));
                }
            }

            mainStack.Children.Add(itemsContainer);

            // Per-section status indicator (only for driveable checklist)
            if (_currentChecklist?.Id == "driveable-checklist")
            {
                var hasRequired = section.Items?.Any(i => i.Required) ?? false;
                if (hasRequired)
                {
                    var statusBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 60, 30, 30)),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0),
                        CornerRadius = new CornerRadius(0, 0, 5, 5)
                    };

                    var statusPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    statusPanel.Children.Add(new FontIcon
                    {
                        Glyph = "\uE7BA",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100))
                    });

                    statusPanel.Children.Add(new TextBlock
                    {
                        Text = "Vehicle should NOT be driven — required items not confirmed",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    statusBorder.Child = statusPanel;
                    mainStack.Children.Add(statusBorder);
                    _sectionStatusIndicators[sectionIndex] = statusBorder;
                }
            }

            border.Child = mainStack;
            return border;
        }

        private Border BuildChecklistItem(ChecklistItem item, bool alternate, int sectionIndex, int itemIndex)
        {
            var itemKey = $"{sectionIndex}_{itemIndex}";

            var border = new Border
            {
                Background = new SolidColorBrush(alternate ? Color.FromArgb(255, 35, 35, 35) : Colors.Transparent),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Interactive CheckBox instead of static border
            var checkBox = new CheckBox
            {
                IsChecked = _checkedItems.Contains(itemKey),
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Track check state changes
            checkBox.Checked += (s, e) =>
            {
                _checkedItems.Add(itemKey);
                UpdateChecklistProgress();
            };
            checkBox.Unchecked += (s, e) =>
            {
                _checkedItems.Remove(itemKey);
                UpdateChecklistProgress();
            };

            Grid.SetColumn(checkBox, 0);

            var text = new TextBlock
            {
                Text = item.Text ?? "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(checkBox);
            grid.Children.Add(text);

            if (item.Required)
            {
                var required = new TextBlock
                {
                    Text = "*",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 80, 80)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Grid.SetColumn(required, 2);
                grid.Children.Add(required);
            }

            border.Child = grid;
            return border;
        }

        // Progress tracking UI elements
        private TextBlock? _progressText;
        private ProgressBar? _progressBar;
        private Dictionary<int, Border> _sectionStatusIndicators = new();

        private void UpdateChecklistProgress()
        {
            if (_currentChecklist?.Sections == null) return;

            var totalItems = _currentChecklist.Sections.Sum(s => s.Items?.Count ?? 0);
            var checkedCount = _checkedItems.Count;
            var requiredItems = _currentChecklist.Sections
                .SelectMany((s, si) => (s.Items ?? new List<ChecklistItem>())
                    .Select((item, ii) => new { Key = $"{si}_{ii}", Required = item.Required }))
                .Where(x => x.Required)
                .ToList();

            var requiredChecked = requiredItems.Count(r => _checkedItems.Contains(r.Key));
            var requiredTotal = requiredItems.Count;

            if (_progressText != null)
            {
                _progressText.Text = $"{checkedCount}/{totalItems} checked ({requiredChecked}/{requiredTotal} required)";
                _progressText.Foreground = new SolidColorBrush(
                    requiredChecked == requiredTotal ? AccentGreen : TextGray);
            }

            if (_progressBar != null)
            {
                _progressBar.Maximum = totalItems;
                _progressBar.Value = checkedCount;
            }

            // Update per-section drivability status indicators (driveable checklist only)
            if (_currentChecklist.Id == "driveable-checklist")
            {
                for (int si = 0; si < _currentChecklist.Sections.Count; si++)
                {
                    if (!_sectionStatusIndicators.TryGetValue(si, out var statusBorder)) continue;

                    var sectionItems = _currentChecklist.Sections[si].Items ?? new List<ChecklistItem>();
                    var allRequiredChecked = true;
                    for (int ii = 0; ii < sectionItems.Count; ii++)
                    {
                        if (sectionItems[ii].Required && !_checkedItems.Contains($"{si}_{ii}"))
                        {
                            allRequiredChecked = false;
                            break;
                        }
                    }

                    if (allRequiredChecked)
                    {
                        statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 20, 50, 30));
                        var panel = (StackPanel)statusBorder.Child;
                        var icon = (FontIcon)panel.Children[0];
                        var text = (TextBlock)panel.Children[1];
                        icon.Glyph = "\uE73E";
                        icon.Foreground = new SolidColorBrush(AccentGreen);
                        text.Text = "PASS — Vehicle is safe to drive";
                        text.Foreground = new SolidColorBrush(AccentGreen);
                    }
                    else
                    {
                        statusBorder.Background = new SolidColorBrush(Color.FromArgb(255, 60, 30, 30));
                        var panel = (StackPanel)statusBorder.Child;
                        var icon = (FontIcon)panel.Children[0];
                        var text = (TextBlock)panel.Children[1];
                        icon.Glyph = "\uE7BA";
                        icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
                        text.Text = "Vehicle should NOT be driven — required items not confirmed";
                        text.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
                    }
                }
            }
        }

        private string GetIconGlyph(string? iconName)
        {
            return iconName switch
            {
                "Folder" => "\uE8B7",
                "Target" => "\uE7C9",
                "Receipt" => "\uE9F9",
                "Ruler" => "\uECC6",
                "Box" => "\uE7B8",
                "Camera" => "\uE722",
                "Paint" => "\uE771",
                "Document" => "\uE8A5",
                "Shield" => "\uE83D",
                "Truck" => "\uE7AC",
                "Warning" => "\uE7BA",
                "Checkmark" => "\uE73E",
                _ => "\uE8A5"
            };
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (_currentChecklist == null) return;

            var text = _checklistService.GeneratePlainText(_currentChecklist);

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);

            if (sender is Button btn)
            {
                var originalContent = btn.Content;
                var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                stack.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
                stack.Children.Add(new TextBlock { Text = "Copied!", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
                btn.Content = stack;

                await System.Threading.Tasks.Task.Delay(1500);
                btn.Content = originalContent;
            }
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (_currentChecklist == null) return;

            var roNumber = _roNumberBox?.Text?.Trim();

            try
            {
                // Generate PDF with checked items
                var pdfPath = _checklistService.GeneratePdf(_currentChecklist, roNumber, _checkedItems);

                // Open the PDF
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                var checkedCount = _checkedItems.Count;
                var totalItems = _currentChecklist.Sections?.Sum(s => s.Items?.Count ?? 0) ?? 0;

                if (sender is Button btn)
                {
                    var originalContent = btn.Content;
                    var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    stack.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
                    stack.Children.Add(new TextBlock { Text = $"PDF Exported! ({checkedCount}/{totalItems} checked)", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
                    btn.Content = stack;

                    await System.Threading.Tasks.Task.Delay(2000);
                    btn.Content = originalContent;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocs] PDF error: {ex.Message}");
            }
        }
    }
}
