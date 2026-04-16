#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using McStudDesktop.Services;
using Microsoft.UI.Dispatching;

namespace McStudDesktop.Views;

/// <summary>
/// Combined Reference View - Definitions, DEG Inquiries, P-Pages, and Procedures all in one tab
/// </summary>
public sealed class ReferenceView : UserControl
{
    // Static instance for navigation from other views
    public static ReferenceView? Instance { get; private set; }

    // Static queue shared for PDF export
    public static List<PdfQueueItem> PdfQueue => DefinitionsView.PdfQueue;

    // Sub-navigation
    private Border? _autoMatchButton;
    private Border? _defsButton;
    private Border? _degButton;
    private Border? _ppagesButton;
    private Border? _procsButton;
    private Border? _inclNotInclButton;
    private Border? _materialsButton;
    private Border? _oemStatementsButton;
    private TextBlock? _autoMatchButtonBadge;
    private int _selectedSection = 0;

    // Content panels
    private Grid? _contentArea;
    private DefinitionsView? _definitionsView;
    private DEGInquiriesView? _degView;
    private PPagesView? _ppagesView;
    private ProceduresView? _proceduresView;
    private IncludedNotIncludedView? _inclNotInclView;
    private MaterialSuggestionsView? _materialsView;
    private OEMPositionStatementsView? _oemStatementsView;


    // Color legend
    private Border? _legendPanel;

    // Staging panel
    private Border? _stagingPanel;
    private StackPanel? _stagingItemsPanel;
    private TextBlock? _stagingCountText;
    private readonly List<StagedReferenceItem> _stagedItems = new();

    // PDF Export panel
    private Border? _pdfPanel;
    private PdfWrapPanel? _pdfSelectedItemsPanel;
    private Border? _pdfItemsClip;
    private Button? _pdfExpandButton;
    private TextBlock? _pdfCountText;
    private TextBlock? _pdfAutoAddedText;
    private Button? _generatePdfButton;
    private PdfExportService? _pdfService;
    private bool _pdfItemsExpanded;
    private const double PdfItemsCollapsedHeight = 40;

    public ReferenceView()
    {
        Instance = this;
        _pdfService = new PdfExportService();
        BuildUI();

        // Subscribe to PDF queue changes from other views
        DefinitionsView.OnItemAddedToPdfQueue += (s, e) =>
        {
            RefreshPdfPanel();
        };
    }

    /// <summary>
    /// Navigate to P-Pages section and search for a specific P-Page
    /// </summary>
    public void NavigateToPPage(string pPageRef)
    {
        SelectSection(3); // P-Pages is index 3 (Auto-Match=0, Defs=1, DEG=2)
        _ppagesView?.SearchFor(pPageRef);
    }

    /// <summary>
    /// Navigate to DEG Inquiries section and search for a specific inquiry
    /// </summary>
    public void NavigateToDEGInquiry(string inquiryNumber)
    {
        SelectSection(2); // DEG is index 2
        _degView?.SearchFor(inquiryNumber);
    }

    /// <summary>
    /// Navigate to Procedures section and search
    /// </summary>
    public void NavigateToProcedure(string searchTerm)
    {
        SelectSection(4); // Procedures is index 4
        _proceduresView?.SearchFor(searchTerm);
    }

    /// <summary>
    /// Navigate to Included/Not Included section and search for a specific operation
    /// </summary>
    public void NavigateToIncludedNotIncluded(string searchTerm)
    {
        SelectSection(5); // Incl/Not Incl is index 5
        _inclNotInclView?.SearchFor(searchTerm);
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sub-nav
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Color legend
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // PDF Panel
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // === SUB-NAVIGATION BAR ===
        var subNavBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        // Use a Grid to have tabs on left, update button on right
        var subNavGrid = new Grid();
        subNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var subNavStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        _autoMatchButton = CreateAutoMatchSubNavButton();
        _defsButton = CreateSubNavButton("Definitions", 1);
        _degButton = CreateSubNavButton("DEG Inquiries", 2);
        _ppagesButton = CreateSubNavButton("P-Pages", 3);
        _procsButton = CreateSubNavButton("How-To Library", 4);
        _inclNotInclButton = CreateSubNavButton("Incl/Not Incl", 5);
        _materialsButton = CreateSubNavButton("Materials", 6);
        _oemStatementsButton = CreateSubNavButton("OEM Statements", 7);

        subNavStack.Children.Add(_autoMatchButton);
        subNavStack.Children.Add(_defsButton);
        subNavStack.Children.Add(_degButton);
        subNavStack.Children.Add(_ppagesButton);
        subNavStack.Children.Add(_procsButton);
        subNavStack.Children.Add(_inclNotInclButton);
        subNavStack.Children.Add(_materialsButton);
        subNavStack.Children.Add(_oemStatementsButton);

        Grid.SetColumn(subNavStack, 0);
        subNavGrid.Children.Add(subNavStack);

        // Color Key toggle button
        var legendToggle = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE790", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)) },
                    new TextBlock { Text = "Color Key", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)), VerticalAlignment = VerticalAlignment.Center }
                }
            },
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        legendToggle.Click += (s, e) =>
        {
            if (_legendPanel != null)
                _legendPanel.Visibility = _legendPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
        };
        Grid.SetColumn(legendToggle, 1);
        subNavGrid.Children.Add(legendToggle);

        subNavBorder.Child = subNavGrid;
        Grid.SetRow(subNavBorder, 0);
        mainGrid.Children.Add(subNavBorder);

        // === COLOR LEGEND (collapsed by default) ===
        _legendPanel = CreateColorLegend();
        Grid.SetRow(_legendPanel, 1);
        mainGrid.Children.Add(_legendPanel);

        // === PDF EXPORT PANEL ===
        _pdfPanel = CreatePdfPanel();
        Grid.SetRow(_pdfPanel, 2);
        mainGrid.Children.Add(_pdfPanel);

        // === CONTENT AREA ===
        _contentArea = new Grid();
        Grid.SetRow(_contentArea, 3);
        mainGrid.Children.Add(_contentArea);

        // Create all views (Auto-Match tab is index 0 — lives inside content area as the first tab)
        _stagingPanel = CreateAutoMatchTabContent();
        _stagingPanel.Tag = 0;
        _stagingPanel.Visibility = Visibility.Visible;

        _definitionsView = new DefinitionsView { Tag = 1, Visibility = Visibility.Collapsed };
        _degView = new DEGInquiriesView { Tag = 2, Visibility = Visibility.Collapsed };
        _ppagesView = new PPagesView { Tag = 3, Visibility = Visibility.Collapsed };
        _proceduresView = new ProceduresView { Tag = 4, Visibility = Visibility.Collapsed };
        _inclNotInclView = new IncludedNotIncludedView { Tag = 5, Visibility = Visibility.Collapsed };
        _materialsView = new MaterialSuggestionsView { Tag = 6, Visibility = Visibility.Collapsed };
        _oemStatementsView = new OEMPositionStatementsView { Tag = 7, Visibility = Visibility.Collapsed };

        _contentArea.Children.Add(_stagingPanel);
        _contentArea.Children.Add(_definitionsView);
        _contentArea.Children.Add(_degView);
        _contentArea.Children.Add(_ppagesView);
        _contentArea.Children.Add(_proceduresView);
        _contentArea.Children.Add(_inclNotInclView);
        _contentArea.Children.Add(_materialsView);
        _contentArea.Children.Add(_oemStatementsView);

        // Select Definitions by default (index 1). Auto-Match auto-selects when matches arrive.
        SelectSection(1);

        // Show empty state in Auto-Match tab
        RefreshStagingPanel();

        Content = mainGrid;
    }

    private Border CreateSubNavButton(string text, int sectionIndex)
    {
        bool isSelected = sectionIndex == _selectedSection;

        var border = new Border
        {
            Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 0, 100, 180)
                : Color.FromArgb(255, 50, 50, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6, 12, 6)
        };

        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected
                ? Colors.White
                : Color.FromArgb(255, 180, 180, 180))
        };

        border.Child = label;

        // Handle click
        border.PointerPressed += (s, e) => SelectSection(sectionIndex);
        border.PointerEntered += (s, e) =>
        {
            if (sectionIndex != _selectedSection)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70));
            }
        };
        border.PointerExited += (s, e) =>
        {
            if (sectionIndex != _selectedSection)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50));
            }
        };

        return border;
    }

    private void SelectSection(int sectionIndex)
    {
        _selectedSection = sectionIndex;

        // Update button styles
        UpdateAutoMatchSubNavButtonStyle();
        UpdateSubNavButtonStyle(_defsButton, 1);
        UpdateSubNavButtonStyle(_degButton, 2);
        UpdateSubNavButtonStyle(_ppagesButton, 3);
        UpdateSubNavButtonStyle(_procsButton, 4);
        UpdateSubNavButtonStyle(_inclNotInclButton, 5);
        UpdateSubNavButtonStyle(_materialsButton, 6);
        UpdateSubNavButtonStyle(_oemStatementsButton, 7);

        // Show selected content
        if (_contentArea == null) return;

        foreach (var child in _contentArea.Children)
        {
            if (child is FrameworkElement element)
            {
                int index = (int)(element.Tag ?? -1);
                element.Visibility = index == sectionIndex ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void UpdateSubNavButtonStyle(Border? button, int sectionIndex)
    {
        if (button == null) return;

        bool isSelected = sectionIndex == _selectedSection;

        button.Background = new SolidColorBrush(isSelected
            ? Color.FromArgb(255, 0, 100, 180)
            : Color.FromArgb(255, 50, 50, 50));

        if (button.Child is TextBlock label)
        {
            label.FontWeight = isSelected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            label.Foreground = new SolidColorBrush(isSelected
                ? Colors.White
                : Color.FromArgb(255, 180, 180, 180));
        }
    }


    // === AUTO-MATCH TAB METHODS ===

    /// <summary>
    /// Creates the Auto-Matched References tab content — header + scrollable item list.
    /// </summary>
    private Border CreateAutoMatchTabContent()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 35, 50)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 6, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
            BorderThickness = new Thickness(1)
        };

        // Inner grid: header row (auto) + scrollable list (star)
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header row
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleStack.Children.Add(new FontIcon
        {
            Glyph = "\uE71C", // Search/match icon
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Auto-Matched References",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        _stagingCountText = new TextBlock
        {
            Text = "(0 items)",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_stagingCountText);
        Grid.SetColumn(titleStack, 0);
        headerRow.Children.Add(titleStack);

        // Action buttons
        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var addSelectedBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 70)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        var addSelContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        addSelContent.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        addSelContent.Children.Add(new TextBlock { Text = "Add Selected to PDF", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
        addSelectedBtn.Content = addSelContent;
        addSelectedBtn.Click += (s, e) => AddSelectedToQueue();
        buttonStack.Children.Add(addSelectedBtn);

        var addAllBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 100, 60)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        var addAllContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        addAllContent.Children.Add(new FontIcon { Glyph = "\uE8B3", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        addAllContent.Children.Add(new TextBlock { Text = "Add All", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
        addAllBtn.Content = addAllContent;
        addAllBtn.Click += (s, e) => AddAllToQueue();
        buttonStack.Children.Add(addAllBtn);

        var clearStagingBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clearContent.Children.Add(new FontIcon { Glyph = "\uE894", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        clearContent.Children.Add(new TextBlock { Text = "Clear", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
        clearStagingBtn.Content = clearContent;
        clearStagingBtn.Click += (s, e) => ClearStaging();
        buttonStack.Children.Add(clearStagingBtn);

        Grid.SetColumn(buttonStack, 1);
        headerRow.Children.Add(buttonStack);
        Grid.SetRow(headerRow, 0);
        rootGrid.Children.Add(headerRow);

        // Scrollable item list
        _stagingItemsPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _stagingItemsPanel
        };
        Grid.SetRow(scroll, 1);
        rootGrid.Children.Add(scroll);

        panel.Child = rootGrid;
        return panel;
    }

    /// <summary>
    /// Creates the Auto-Match sub-nav button with a count badge that updates as matches come in.
    /// </summary>
    private Border CreateAutoMatchSubNavButton()
    {
        bool isSelected = _selectedSection == 0;

        var border = new Border
        {
            Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 0, 100, 180)
                : Color.FromArgb(255, 40, 60, 90)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE71C",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Auto-Match",
            FontSize = 12,
            FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(255, 190, 210, 240)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _autoMatchButtonBadge = new TextBlock
        {
            Text = "",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(_autoMatchButtonBadge);

        border.Child = stack;
        border.PointerPressed += (s, e) => SelectSection(0);
        border.PointerEntered += (s, e) =>
        {
            if (_selectedSection != 0)
                border.Background = new SolidColorBrush(Color.FromArgb(255, 60, 90, 130));
        };
        border.PointerExited += (s, e) =>
        {
            if (_selectedSection != 0)
                border.Background = new SolidColorBrush(Color.FromArgb(255, 40, 60, 90));
        };

        return border;
    }

    private void UpdateAutoMatchSubNavButtonStyle()
    {
        if (_autoMatchButton == null) return;

        bool isSelected = _selectedSection == 0;
        _autoMatchButton.Background = new SolidColorBrush(isSelected
            ? Color.FromArgb(255, 0, 100, 180)
            : Color.FromArgb(255, 40, 60, 90));

        if (_autoMatchButton.Child is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is TextBlock tb && tb != _autoMatchButtonBadge)
                {
                    tb.FontWeight = isSelected
                        ? Microsoft.UI.Text.FontWeights.SemiBold
                        : Microsoft.UI.Text.FontWeights.Normal;
                    tb.Foreground = new SolidColorBrush(isSelected
                        ? Colors.White
                        : Color.FromArgb(255, 190, 210, 240));
                }
            }
        }
    }

    /// <summary>
    /// Show staged reference items for user review. Merges new items, skips duplicates.
    /// Auto-navigates to the Auto-Match tab so the user sees the new matches immediately.
    /// </summary>
    public void ShowStagedItems(List<StagedReferenceItem> items)
    {
        if (items == null || items.Count == 0) return;

        // Marshal to UI thread
        DispatcherQueue?.TryEnqueue(() =>
        {
            // Merge: skip items already staged or already in PDF queue
            bool addedAny = false;
            foreach (var item in items)
            {
                if (_stagedItems.Any(s => s.QueueItem.Id == item.QueueItem.Id))
                    continue;
                if (DefinitionsView.PdfQueue.Any(q => q.Id == item.QueueItem.Id))
                    continue;
                _stagedItems.Add(item);
                addedAny = true;
            }

            RefreshStagingPanel();

            // Auto-navigate to Auto-Match tab so user sees the new matches
            if (addedAny)
                SelectSection(0);
        });
    }

    private void RefreshStagingPanel()
    {
        if (_stagingItemsPanel == null || _stagingCountText == null) return;

        _stagingItemsPanel.Children.Clear();
        _stagingCountText.Text = $"({_stagedItems.Count} items)";

        // Update sub-nav badge
        if (_autoMatchButtonBadge != null)
        {
            if (_stagedItems.Count > 0)
            {
                _autoMatchButtonBadge.Text = $"({_stagedItems.Count})";
                _autoMatchButtonBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _autoMatchButtonBadge.Visibility = Visibility.Collapsed;
            }
        }

        if (_stagedItems.Count == 0)
        {
            // Empty state message
            _stagingItemsPanel.Children.Add(new TextBlock
            {
                Text = "No auto-matched references yet. Upload an estimate in the Import tab or use the Screen Monitor to auto-match reference content.",
                FontSize = 12,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            return;
        }

        // Group by MatchSource
        var groups = _stagedItems.GroupBy(s => s.MatchSource).OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            // Group header
            var groupHeader = new TextBlock
            {
                Text = group.Key,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GetSourceColor(group.Key)),
                Margin = new Thickness(0, 6, 0, 2)
            };
            _stagingItemsPanel.Children.Add(groupHeader);

            // Items in this group
            foreach (var staged in group)
            {
                var row = CreateStagedItemRow(staged);
                _stagingItemsPanel.Children.Add(row);
            }
        }
    }

    private Grid CreateStagedItemRow(StagedReferenceItem staged)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 1, 0, 1)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Source badge
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Term + preview

        // Checkbox
        var cb = new CheckBox
        {
            IsChecked = staged.IsSelected,
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (s, e) => staged.IsSelected = true;
        cb.Unchecked += (s, e) => staged.IsSelected = false;
        Grid.SetColumn(cb, 0);
        row.Children.Add(cb);

        // Source badge
        var badge = new Border
        {
            Background = new SolidColorBrush(GetSourceColor(staged.MatchSource)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = staged.MatchSource,
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        // Term name + truncated definition
        var textStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(new TextBlock
        {
            Text = staged.QueueItem.Term.Length > 40
                ? staged.QueueItem.Term[..37] + "..."
                : staged.QueueItem.Term,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Truncated definition preview
        var defPreview = staged.QueueItem.Definition;
        if (!string.IsNullOrEmpty(defPreview))
        {
            // Take first line, truncate
            var firstLine = defPreview.Split('\n')[0].Trim();
            if (firstLine.Length > 60)
                firstLine = firstLine[..57] + "...";

            textStack.Children.Add(new TextBlock
            {
                Text = firstLine,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(textStack, 2);
        row.Children.Add(textStack);

        return row;
    }

    private void AddSelectedToQueue()
    {
        var selected = _stagedItems.Where(s => s.IsSelected).ToList();
        foreach (var staged in selected)
        {
            if (!DefinitionsView.PdfQueue.Any(q => q.Id == staged.QueueItem.Id))
            {
                DefinitionsView.PdfQueue.Add(staged.QueueItem);
                DefinitionsView.RaiseItemAddedToPdfQueue(staged.QueueItem);
            }
        }

        // Remove added items from staging
        _stagedItems.RemoveAll(s => s.IsSelected);
        RefreshStagingPanel();
        RefreshPdfPanel();
    }

    private void AddAllToQueue()
    {
        foreach (var staged in _stagedItems)
        {
            if (!DefinitionsView.PdfQueue.Any(q => q.Id == staged.QueueItem.Id))
            {
                DefinitionsView.PdfQueue.Add(staged.QueueItem);
                DefinitionsView.RaiseItemAddedToPdfQueue(staged.QueueItem);
            }
        }

        _stagedItems.Clear();
        RefreshStagingPanel();
        RefreshPdfPanel();
    }

    public void ClearStaging()
    {
        _stagedItems.Clear();
        RefreshStagingPanel();
    }

    private static Color GetSourceColor(string source) => source switch
    {
        "Included/Not Included" => Color.FromArgb(255, 60, 130, 80),
        "Definition" => Color.FromArgb(255, 80, 120, 200),
        "P-Page" => Color.FromArgb(255, 160, 100, 50),
        "DEG Inquiry" => Color.FromArgb(255, 140, 80, 160),
        "Procedure" => Color.FromArgb(255, 60, 140, 140),
        "Cross-Reference" => Color.FromArgb(255, 130, 130, 60),
        _ => Color.FromArgb(255, 100, 100, 100)
    };

    private Border CreateColorLegend()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
            Padding = new Thickness(12, 6, 12, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var wrap = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16
        };

        // Status dots label
        wrap.Children.Add(new TextBlock
        {
            Text = "Status:",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Green dot = Included
        var greenChip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        greenChip.Children.Add(new Border
        {
            Width = 10, Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
            VerticalAlignment = VerticalAlignment.Center
        });
        greenChip.Children.Add(new TextBlock
        {
            Text = "Base Operation",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100)),
            VerticalAlignment = VerticalAlignment.Center
        });
        wrap.Children.Add(greenChip);

        // Red dot = Not Included
        var redChip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        redChip.Children.Add(new Border
        {
            Width = 10, Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
            VerticalAlignment = VerticalAlignment.Center
        });
        redChip.Children.Add(new TextBlock
        {
            Text = "Not Included",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100)),
            VerticalAlignment = VerticalAlignment.Center
        });
        wrap.Children.Add(redChip);

        // Divider
        wrap.Children.Add(new Border
        {
            Width = 1, Height = 16,
            Background = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        });

        // Source badges label
        wrap.Children.Add(new TextBlock
        {
            Text = "Source:",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Source badge categories
        var categories = new (string Name, Color Color)[]
        {
            ("Included/Not Included", GetSourceColor("Included/Not Included")),
            ("Definition", GetSourceColor("Definition")),
            ("P-Page", GetSourceColor("P-Page")),
            ("DEG Inquiry", GetSourceColor("DEG Inquiry")),
            ("Procedure", GetSourceColor("Procedure")),
            ("Cross-Reference", GetSourceColor("Cross-Reference"))
        };

        foreach (var (name, color) in categories)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };

            chip.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            });

            chip.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 190, 190, 190)),
                VerticalAlignment = VerticalAlignment.Center
            });

            wrap.Children.Add(chip);
        }

        panel.Child = wrap;
        return panel;
    }

    // === PDF EXPORT PANEL METHODS ===

    /// <summary>
    /// Creates the PDF export panel that shows at the top of Reference view
    /// </summary>
    private Border CreatePdfPanel()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 25, 25)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 60, 60)),
            BorderThickness = new Thickness(1)
        };

        var mainStack = new StackPanel();

        // Header row with title and buttons
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Title with count
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleStack.Children.Add(new FontIcon
        {
            Glyph = "\uE8A5", // PDF icon
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100))
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "PDF Export Queue",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        _pdfCountText = new TextBlock
        {
            Text = $"({PdfQueue.Count} items)",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_pdfCountText);
        Grid.SetColumn(titleStack, 0);
        headerRow.Children.Add(titleStack);

        // Buttons
        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Generate PDF button
        _generatePdfButton = new Button
        {
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 150, 50, 50)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        var genContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        genContent.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        genContent.Children.Add(new TextBlock { Text = "Generate PDF", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
        _generatePdfButton.Content = genContent;
        _generatePdfButton.Click += GeneratePdf_Click;
        buttonStack.Children.Add(_generatePdfButton);

        // PDF Settings gear button
        var settingsBtn = new Button
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        settingsBtn.Content = new FontIcon { Glyph = "\uE713", FontSize = 14, Foreground = new SolidColorBrush(Colors.White) };
        ToolTipService.SetToolTip(settingsBtn, "PDF Export Settings");
        settingsBtn.Click += OpenPdfSettings_Click;
        buttonStack.Children.Add(settingsBtn);

        // Expand / collapse toggle for the items row
        _pdfExpandButton = new Button
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        _pdfExpandButton.Content = new FontIcon
        {
            Glyph = "\uE70D", // ChevronDown
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White)
        };
        ToolTipService.SetToolTip(_pdfExpandButton, "Expand queue");
        _pdfExpandButton.Click += PdfExpandToggle_Click;
        buttonStack.Children.Add(_pdfExpandButton);

        // Clear button
        var clearBtn = new Button
        {
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clearContent.Children.Add(new FontIcon { Glyph = "\uE894", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        clearContent.Children.Add(new TextBlock { Text = "Clear", FontSize = 11, Foreground = new SolidColorBrush(Colors.White) });
        clearBtn.Content = clearContent;
        clearBtn.Click += ClearPdfQueue_Click;
        buttonStack.Children.Add(clearBtn);

        Grid.SetColumn(buttonStack, 1);
        headerRow.Children.Add(buttonStack);
        mainStack.Children.Add(headerRow);

        // Selected items panel — a wrap panel (items flow to new lines when the row fills)
        // clipped to one-row height by default with an expand/collapse toggle.
        _pdfSelectedItemsPanel = new PdfWrapPanel
        {
            HorizontalSpacing = 6,
            VerticalSpacing = 6
        };
        _pdfItemsClip = new Border
        {
            MaxHeight = PdfItemsCollapsedHeight,
            Margin = new Thickness(0, 8, 0, 0),
            Child = _pdfSelectedItemsPanel
        };
        _pdfItemsClip.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry();
        _pdfItemsClip.SizeChanged += (s, e) =>
        {
            if (_pdfItemsClip?.Clip is Microsoft.UI.Xaml.Media.RectangleGeometry rg)
            {
                rg.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            }
        };
        mainStack.Children.Add(_pdfItemsClip);

        // Auto-added status text
        _pdfAutoAddedText = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 150)),
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0)
        };
        mainStack.Children.Add(_pdfAutoAddedText);

        // Initial state
        RefreshPdfPanel();

        panel.Child = mainStack;
        return panel;
    }

    private void RefreshPdfPanel()
    {
        if (_pdfSelectedItemsPanel == null || _pdfCountText == null) return;

        _pdfSelectedItemsPanel.Children.Clear();
        _pdfCountText.Text = $"({PdfQueue.Count} items)";

        if (PdfQueue.Count == 0)
        {
            _pdfSelectedItemsPanel.Children.Add(new TextBlock
            {
                Text = "Click 'Add to PDF' on any item to add it here",
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            });
        }
        else
        {
            foreach (var item in PdfQueue)
            {
                var chip = CreatePdfItemChip(item);
                _pdfSelectedItemsPanel.Children.Add(chip);
            }
        }
    }

    private Border CreatePdfItemChip(PdfQueueItem item)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 80, 60)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        stack.Children.Add(new TextBlock
        {
            Text = item.Term,
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        });

        // Remove button (X)
        var removeBtn = new Button
        {
            Content = "\u2715",
            FontSize = 10,
            Padding = new Thickness(2),
            MinWidth = 16,
            MinHeight = 16,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
        };
        removeBtn.Click += (s, e) =>
        {
            PdfQueue.RemoveAll(q => q.Id == item.Id);
            RefreshPdfPanel();
            DefinitionsView.RaisePdfQueueChanged();
        };
        stack.Children.Add(removeBtn);

        chip.Child = stack;
        return chip;
    }

    private void GeneratePdf_Click(object sender, RoutedEventArgs e)
    {
        if (PdfQueue.Count == 0)
        {
            return;
        }

        try
        {
            if (_pdfService == null) _pdfService = new PdfExportService();

            // Convert PdfQueueItem to PdfExportItem
            var exportItems = PdfQueue.Select(q => new PdfExportItem
            {
                Id = q.Id,
                Term = q.Term,
                Category = q.Category,
                Definition = q.Definition,
                Details = q.Details,
                PPageRef = q.PPageRef,
                PPageLocation = q.PPageLocation,
                DegInquiry = q.DegInquiry,
                DegResponse = q.DegResponse,
                Status = q.Status
            }).ToList();

            var outputPath = _pdfService.GeneratePdfFromItems(exportItems);

            // Open the PDF
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = outputPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReferenceView] PDF generation error: {ex.Message}");
        }
    }

    private async void OpenPdfSettings_Click(object sender, RoutedEventArgs e)
    {
        var configService = ReferenceExportConfigService.Instance;
        var config = configService.Config;

        // Clone current values so we can discard on cancel
        var titleBox = new TextBox
        {
            Text = config.HeaderTitle,
            Header = "Header Title",
            PlaceholderText = "MET Reference Guide",
            Margin = new Thickness(0, 0, 0, 10)
        };

        var subtitleBox = new TextBox
        {
            Text = config.HeaderSubtitle,
            Header = "Header Subtitle",
            PlaceholderText = "Collision Estimating Reference Documentation",
            Margin = new Thickness(0, 0, 0, 10)
        };

        var showDateToggle = new ToggleSwitch
        {
            Header = "Show Date & Time",
            IsOn = config.ShowDate,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var dateFormatBox = new TextBox
        {
            Text = config.DateFormat,
            Header = "Date Format",
            PlaceholderText = "MMMM dd, yyyy 'at' h:mm tt",
            IsEnabled = config.ShowDate,
            Margin = new Thickness(0, 0, 0, 10)
        };

        showDateToggle.Toggled += (s, args) =>
        {
            dateFormatBox.IsEnabled = showDateToggle.IsOn;
        };

        var footerBox = new TextBox
        {
            Text = config.FooterText,
            Header = "Footer Text",
            PlaceholderText = "MET Reference Guide",
            Margin = new Thickness(0, 0, 0, 10)
        };

        var showTocToggle = new ToggleSwitch
        {
            Header = "Show Table of Contents",
            IsOn = config.ShowTableOfContents,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var showPageNumbersToggle = new ToggleSwitch
        {
            Header = "Show Page Numbers",
            IsOn = config.ShowPageNumbers,
            Margin = new Thickness(0, 0, 0, 10)
        };

        // Reset to Defaults button
        var resetButton = new HyperlinkButton
        {
            Content = "Reset to Defaults",
            Margin = new Thickness(0, 4, 0, 0)
        };
        resetButton.Click += (s, args) =>
        {
            var defaults = new Services.ReferenceExportConfig();
            titleBox.Text = defaults.HeaderTitle;
            subtitleBox.Text = defaults.HeaderSubtitle;
            showDateToggle.IsOn = defaults.ShowDate;
            dateFormatBox.Text = defaults.DateFormat;
            footerBox.Text = defaults.FooterText;
            showTocToggle.IsOn = defaults.ShowTableOfContents;
            showPageNumbersToggle.IsOn = defaults.ShowPageNumbers;
        };

        var contentPanel = new StackPanel
        {
            Width = 380,
            Children =
            {
                titleBox,
                subtitleBox,
                showDateToggle,
                dateFormatBox,
                footerBox,
                showTocToggle,
                showPageNumbersToggle,
                resetButton
            }
        };

        var dialog = new ContentDialog
        {
            Title = "PDF Export Settings",
            Content = contentPanel,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            config.HeaderTitle = titleBox.Text;
            config.HeaderSubtitle = subtitleBox.Text;
            config.ShowDate = showDateToggle.IsOn;
            config.DateFormat = dateFormatBox.Text;
            config.FooterText = footerBox.Text;
            config.ShowTableOfContents = showTocToggle.IsOn;
            config.ShowPageNumbers = showPageNumbersToggle.IsOn;
            configService.SaveConfig();
        }
    }

    private void ClearPdfQueue_Click(object sender, RoutedEventArgs e)
    {
        PdfQueue.Clear();
        RefreshPdfPanel();
        DefinitionsView.RaisePdfQueueChanged();
    }

    private void PdfExpandToggle_Click(object sender, RoutedEventArgs e)
    {
        _pdfItemsExpanded = !_pdfItemsExpanded;
        if (_pdfItemsClip != null)
        {
            _pdfItemsClip.MaxHeight = _pdfItemsExpanded ? double.PositiveInfinity : PdfItemsCollapsedHeight;
        }
        if (_pdfExpandButton != null)
        {
            _pdfExpandButton.Content = new FontIcon
            {
                Glyph = _pdfItemsExpanded ? "\uE70E" : "\uE70D", // ChevronUp / ChevronDown
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White)
            };
            ToolTipService.SetToolTip(_pdfExpandButton, _pdfItemsExpanded ? "Collapse queue" : "Expand queue");
        }
    }
}

// Simple wrap panel — items flow horizontally and wrap to new rows when
// they exceed the available width. Used by the PDF export queue.
internal sealed class PdfWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }
    public double VerticalSpacing { get; set; }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, maxRowWidth = 0;
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

        foreach (UIElement child in Children)
        {
            child.Measure(new Windows.Foundation.Size(maxWidth, double.PositiveInfinity));
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > maxWidth)
            {
                y += rowHeight + VerticalSpacing;
                maxRowWidth = Math.Max(maxRowWidth, x - HorizontalSpacing);
                x = 0;
                rowHeight = 0;
            }

            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        maxRowWidth = Math.Max(maxRowWidth, x - HorizontalSpacing);
        return new Windows.Foundation.Size(
            double.IsInfinity(availableSize.Width) ? maxRowWidth : availableSize.Width,
            y + rowHeight);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;

        foreach (UIElement child in Children)
        {
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Windows.Foundation.Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }

        return finalSize;
    }
}
