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
    private Border? _defsButton;
    private Border? _degButton;
    private Border? _ppagesButton;
    private Border? _procsButton;
    private Border? _inclNotInclButton;
    private int _selectedSection = 0;

    // Content panels
    private Grid? _contentArea;
    private DefinitionsView? _definitionsView;
    private DEGInquiriesView? _degView;
    private PPagesView? _ppagesView;
    private ProceduresView? _proceduresView;
    private IncludedNotIncludedView? _inclNotInclView;
    private Border? _materialsButton;
    private MaterialSuggestionsView? _materialsView;

    // Update UI elements
    private Button? _updateButton;
    private TextBlock? _updateStatusText;
    private ProgressRing? _updateProgress;
    private readonly DataUpdateService _dataUpdateService = new();

    // Staging panel
    private Border? _stagingPanel;
    private StackPanel? _stagingItemsPanel;
    private TextBlock? _stagingCountText;
    private readonly List<StagedReferenceItem> _stagedItems = new();

    // PDF Export panel
    private Border? _pdfPanel;
    private StackPanel? _pdfSelectedItemsPanel;
    private TextBlock? _pdfCountText;
    private TextBlock? _pdfAutoAddedText;
    private Button? _generatePdfButton;
    private PdfExportService? _pdfService;

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
        SelectSection(2); // P-Pages is index 2
        _ppagesView?.SearchFor(pPageRef);
    }

    /// <summary>
    /// Navigate to DEG Inquiries section and search for a specific inquiry
    /// </summary>
    public void NavigateToDEGInquiry(string inquiryNumber)
    {
        SelectSection(1); // DEG is index 1
        _degView?.SearchFor(inquiryNumber);
    }

    /// <summary>
    /// Navigate to Procedures section and search
    /// </summary>
    public void NavigateToProcedure(string searchTerm)
    {
        SelectSection(3); // Procedures is index 3
        _proceduresView?.SearchFor(searchTerm);
    }

    /// <summary>
    /// Navigate to Included/Not Included section and search for a specific operation
    /// </summary>
    public void NavigateToIncludedNotIncluded(string searchTerm)
    {
        SelectSection(4); // Incl/Not Incl is index 4
        _inclNotInclView?.SearchFor(searchTerm);
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sub-nav
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Staging Panel
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

        _defsButton = CreateSubNavButton("Definitions", 0);
        _degButton = CreateSubNavButton("DEG Inquiries", 1);
        _ppagesButton = CreateSubNavButton("P-Pages", 2);
        _procsButton = CreateSubNavButton("Procedures", 3);
        _inclNotInclButton = CreateSubNavButton("Incl/Not Incl", 4);
        _materialsButton = CreateSubNavButton("Materials", 5);

        subNavStack.Children.Add(_defsButton);
        subNavStack.Children.Add(_degButton);
        subNavStack.Children.Add(_ppagesButton);
        subNavStack.Children.Add(_procsButton);
        subNavStack.Children.Add(_inclNotInclButton);
        subNavStack.Children.Add(_materialsButton);

        Grid.SetColumn(subNavStack, 0);
        subNavGrid.Children.Add(subNavStack);

        // Update Data button and status (on the right)
        var updatePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        _updateStatusText = new TextBlock
        {
            Text = "",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center
        };
        updatePanel.Children.Add(_updateStatusText);

        _updateProgress = new ProgressRing
        {
            Width = 16,
            Height = 16,
            IsActive = false,
            Visibility = Visibility.Collapsed
        };
        updatePanel.Children.Add(_updateProgress);

        _updateButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE895", FontSize = 12 }, // Sync icon
                    new TextBlock { Text = "Update Data", FontSize = 11 }
                }
            },
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 70)),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        ToolTipService.SetToolTip(_updateButton, "Check for updates to P-Pages, DEG Inquiries, and Definitions");
        _updateButton.Click += OnUpdateButtonClick;
        updatePanel.Children.Add(_updateButton);

        Grid.SetColumn(updatePanel, 1);
        subNavGrid.Children.Add(updatePanel);

        subNavBorder.Child = subNavGrid;
        Grid.SetRow(subNavBorder, 0);
        mainGrid.Children.Add(subNavBorder);

        // === STAGING PANEL (collapsed by default) ===
        _stagingPanel = CreateStagingPanel();
        Grid.SetRow(_stagingPanel, 1);
        mainGrid.Children.Add(_stagingPanel);

        // === PDF EXPORT PANEL ===
        _pdfPanel = CreatePdfPanel();
        Grid.SetRow(_pdfPanel, 2);
        mainGrid.Children.Add(_pdfPanel);

        // === CONTENT AREA ===
        _contentArea = new Grid();
        Grid.SetRow(_contentArea, 3);
        mainGrid.Children.Add(_contentArea);

        // Create all views
        _definitionsView = new DefinitionsView { Tag = 0, Visibility = Visibility.Visible };
        _degView = new DEGInquiriesView { Tag = 1, Visibility = Visibility.Collapsed };
        _ppagesView = new PPagesView { Tag = 2, Visibility = Visibility.Collapsed };
        _proceduresView = new ProceduresView { Tag = 3, Visibility = Visibility.Collapsed };
        _inclNotInclView = new IncludedNotIncludedView { Tag = 4, Visibility = Visibility.Collapsed };
        _materialsView = new MaterialSuggestionsView { Tag = 5, Visibility = Visibility.Collapsed };

        _contentArea.Children.Add(_definitionsView);
        _contentArea.Children.Add(_degView);
        _contentArea.Children.Add(_ppagesView);
        _contentArea.Children.Add(_proceduresView);
        _contentArea.Children.Add(_inclNotInclView);
        _contentArea.Children.Add(_materialsView);

        // Select first section
        SelectSection(0);

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
        UpdateSubNavButtonStyle(_defsButton, 0);
        UpdateSubNavButtonStyle(_degButton, 1);
        UpdateSubNavButtonStyle(_ppagesButton, 2);
        UpdateSubNavButtonStyle(_procsButton, 3);
        UpdateSubNavButtonStyle(_inclNotInclButton, 4);
        UpdateSubNavButtonStyle(_materialsButton, 5);

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

    private async void OnUpdateButtonClick(object sender, RoutedEventArgs e)
    {
        if (_updateButton == null || _updateStatusText == null || _updateProgress == null)
            return;

        var dispatcher = DispatcherQueue.GetForCurrentThread();

        // Disable button and show progress
        _updateButton.IsEnabled = false;
        _updateProgress.IsActive = true;
        _updateProgress.Visibility = Visibility.Visible;
        _updateStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));

        // Subscribe to status updates
        _dataUpdateService.StatusChanged += status =>
        {
            dispatcher?.TryEnqueue(() =>
            {
                if (_updateStatusText != null)
                    _updateStatusText.Text = status;
            });
        };

        try
        {
            // Check and download updates
            var result = await _dataUpdateService.UpdateAllAsync();

            dispatcher?.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    if (result.UpdatedFiles.Count > 0)
                    {
                        _updateStatusText!.Text = $"Updated {result.UpdatedFiles.Count} file(s). Restart app to see changes.";
                        _updateStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 100));
                    }
                    else
                    {
                        _updateStatusText!.Text = result.Message ?? "All data is up to date!";
                        _updateStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
                    }
                }
                else
                {
                    var errorMsg = result.Errors.Count > 0 ? result.Errors[0] : "Update failed";
                    _updateStatusText!.Text = errorMsg;
                    _updateStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
                }
            });
        }
        catch (Exception ex)
        {
            dispatcher?.TryEnqueue(() =>
            {
                _updateStatusText!.Text = $"Error: {ex.Message}";
                _updateStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
            });
        }
        finally
        {
            dispatcher?.TryEnqueue(() =>
            {
                _updateButton!.IsEnabled = true;
                _updateProgress!.IsActive = false;
                _updateProgress.Visibility = Visibility.Collapsed;
            });
        }
    }

    // === STAGING PANEL METHODS ===

    /// <summary>
    /// Creates the Auto-Matched References staging panel (collapsed by default)
    /// </summary>
    private Border CreateStagingPanel()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 35, 50)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 6, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 100, 160)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed
        };

        var mainStack = new StackPanel();

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
        mainStack.Children.Add(headerRow);

        // Staged items list (grouped by source)
        _stagingItemsPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 8, 0, 0)
        };
        mainStack.Children.Add(_stagingItemsPanel);

        panel.Child = mainStack;
        return panel;
    }

    /// <summary>
    /// Show staged reference items for user review. Merges new items, skips duplicates.
    /// </summary>
    public void ShowStagedItems(List<StagedReferenceItem> items)
    {
        if (items == null || items.Count == 0) return;

        // Marshal to UI thread
        DispatcherQueue?.TryEnqueue(() =>
        {
            // Merge: skip items already staged or already in PDF queue
            foreach (var item in items)
            {
                if (_stagedItems.Any(s => s.QueueItem.Id == item.QueueItem.Id))
                    continue;
                if (DefinitionsView.PdfQueue.Any(q => q.Id == item.QueueItem.Id))
                    continue;
                _stagedItems.Add(item);
            }

            RefreshStagingPanel();
        });
    }

    private void RefreshStagingPanel()
    {
        if (_stagingPanel == null || _stagingItemsPanel == null || _stagingCountText == null) return;

        _stagingItemsPanel.Children.Clear();
        _stagingCountText.Text = $"({_stagedItems.Count} items)";

        if (_stagedItems.Count == 0)
        {
            _stagingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _stagingPanel.Visibility = Visibility.Visible;

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

    private void ClearStaging()
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

        // Selected items panel (horizontal wrap)
        _pdfSelectedItemsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        mainStack.Children.Add(_pdfSelectedItemsPanel);

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
            Text = item.Term.Length > 25 ? item.Term.Substring(0, 22) + "..." : item.Term,
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
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

    private void ClearPdfQueue_Click(object sender, RoutedEventArgs e)
    {
        PdfQueue.Clear();
        RefreshPdfPanel();
        DefinitionsView.RaisePdfQueueChanged();
    }
}
