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

    // PDF Export panel
    private Border? _pdfPanel;
    private StackPanel? _pdfSelectedItemsPanel;
    private TextBlock? _pdfCountText;
    private Button? _generatePdfButton;
    private PdfExportService? _pdfService;

    public ReferenceView()
    {
        Instance = this;
        _pdfService = new PdfExportService();
        BuildUI();

        // Subscribe to PDF queue changes from other views
        DefinitionsView.OnItemAddedToPdfQueue += (s, e) => RefreshPdfPanel();
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

        // === PDF EXPORT PANEL ===
        _pdfPanel = CreatePdfPanel();
        Grid.SetRow(_pdfPanel, 1);
        mainGrid.Children.Add(_pdfPanel);

        // === CONTENT AREA ===
        _contentArea = new Grid();
        Grid.SetRow(_contentArea, 2);
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
    }
}
