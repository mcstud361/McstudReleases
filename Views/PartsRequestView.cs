#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

public class PartsRequestView : UserControl
{
    private static readonly Color AccentGreen = Color.FromArgb(255, 0, 180, 80);
    private static readonly Color DarkBg = Color.FromArgb(255, 30, 30, 30);
    private static readonly Color PanelBg = Color.FromArgb(255, 40, 40, 40);
    private static readonly Color BorderColor = Color.FromArgb(255, 60, 60, 60);

    private readonly PartsRequestService _service = PartsRequestService.Instance;
    private PartsRequest? _currentRequest;
    private StackPanel? _partsListPanel;
    private TextBox? _roBox;
    private TextBlock? _dateText;
    private InfoBar? _infoBar;
    private DispatcherTimer? _saveTimer;
    private TextBlock? _lineCountText;
    private TextBox? _headerBox;
    private TextBox? _subHeaderBox;

    // Default number of write-in rows a fresh/empty request starts with (fills one Letter page).
    private const int DefaultPdfLines = 22;

    public PartsRequestView()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (s, e) =>
        {
            _saveTimer.Stop();
            SaveCurrent();
        };
        BuildUI();
        LoadRequests();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(DarkBg),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },   // Header
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Table
                new RowDefinition { Height = GridLength.Auto }    // Footer
            }
        };

        // ── Header ──
        var header = new Border
        {
            Background = new SolidColorBrush(PanelBg),
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        // RO #
        headerPanel.Children.Add(new TextBlock
        {
            Text = "RO #:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        });
        _roBox = new TextBox
        {
            PlaceholderText = "RO number",
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center
        };
        _roBox.TextChanged += (s, e) => DebounceSave();
        headerPanel.Children.Add(_roBox);

        // Date
        _dateText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        };
        headerPanel.Children.Add(_dateText);

        // Second header row: customizable title + sub-header printed on the PDF sheet.
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Header:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        });
        _headerBox = new TextBox
        {
            PlaceholderText = "Parts Request",
            MinWidth = 200,
            VerticalAlignment = VerticalAlignment.Center
        };
        _headerBox.TextChanged += (s, e) =>
        {
            if (_currentRequest != null) { _currentRequest.Header = _headerBox.Text; DebounceSave(); }
        };
        titleRow.Children.Add(_headerBox);

        titleRow.Children.Add(new TextBlock
        {
            Text = "Sub-header:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        });
        _subHeaderBox = new TextBox
        {
            PlaceholderText = "Optional line under the title",
            MinWidth = 260,
            VerticalAlignment = VerticalAlignment.Center
        };
        _subHeaderBox.TextChanged += (s, e) =>
        {
            if (_currentRequest != null) { _currentRequest.SubHeader = _subHeaderBox.Text; DebounceSave(); }
        };
        titleRow.Children.Add(_subHeaderBox);

        var headerStack = new StackPanel { Spacing = 0 };
        headerStack.Children.Add(headerPanel);
        headerStack.Children.Add(titleRow);

        header.Child = headerStack;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // ── Table area ──
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16)
        };

        var tableContainer = new StackPanel { Spacing = 0 };

        // Line stepper — sits ABOVE the table. Adds/removes real rows you can see and fill in,
        // and those are exactly what prints on the PDF.
        var linesToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        linesToolbar.Children.Add(new TextBlock
        {
            Text = "Lines:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        var minusBtn = new Button
        {
            Content = "− 5",
            Padding = new Thickness(12, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(minusBtn, "Remove 5 blank lines");
        minusBtn.Click += OnRemoveLines;
        linesToolbar.Children.Add(minusBtn);
        _lineCountText = new TextBlock
        {
            Text = "0",
            MinWidth = 28,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        linesToolbar.Children.Add(_lineCountText);
        var plusBtn = new Button
        {
            Content = "+ 5",
            Padding = new Thickness(12, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(plusBtn, "Add 5 blank lines");
        plusBtn.Click += OnAddLines;
        linesToolbar.Children.Add(plusBtn);
        tableContainer.Children.Add(linesToolbar);

        // Column headers
        var headerRow = CreateColumnHeaderRow();
        tableContainer.Children.Add(headerRow);

        // Parts list
        _partsListPanel = new StackPanel { Spacing = 2 };
        tableContainer.Children.Add(_partsListPanel);

        // Add Part button
        var addBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 14 },
                    new TextBlock { Text = "Add Part", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        addBtn.Click += OnAddPart;
        tableContainer.Children.Add(addBtn);

        scrollViewer.Content = tableContainer;
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(scrollViewer);

        // ── Footer ──
        var footer = new Border
        {
            Background = new SolidColorBrush(PanelBg),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Summary
        var summaryPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        summaryPanel.Children.Add(new TextBlock
        {
            Text = "0 parts",
            Tag = "partCount",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(summaryPanel, 0);
        footerGrid.Children.Add(summaryPanel);

        // Clear Form
        var clearContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clearContent.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 12 });
        clearContent.Children.Add(new TextBlock { Text = "Clear Form", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var clearBtn = new Button
        {
            Content = clearContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        clearBtn.Click += OnClear;
        Grid.SetColumn(clearBtn, 1);
        footerGrid.Children.Add(clearBtn);


        // Export to PDF
        var exportContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        exportContent.Children.Add(new FontIcon { Glyph = "\uE749", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        exportContent.Children.Add(new TextBlock { Text = "Export to PDF", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) });

        var exportBtn = new Button
        {
            Content = exportContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(AccentGreen)
        };
        exportBtn.Click += OnExport;
        Grid.SetColumn(exportBtn, 2);
        footerGrid.Children.Add(exportBtn);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        mainGrid.Children.Add(footer);

        // InfoBar
        _infoBar = new InfoBar
        {
            IsOpen = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0)
        };
        mainGrid.Children.Add(_infoBar);

        Content = mainGrid;
    }

    private Border CreateColumnHeaderRow()
    {
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Padding = new Thickness(8, 8, 8, 8)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });  // Description
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // Part #
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) }); // Qty
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });  // Notes
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) }); // Delete

        var labels = new[] { "Description", "Part #", "Qty", "Notes", "" };
        for (int i = 0; i < labels.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = labels[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(tb, i);
            headerGrid.Children.Add(tb);
        }

        headerBorder.Child = headerGrid;
        return headerBorder;
    }

    private Border CreatePartRow(PartsRequestItem item)
    {
        var rowBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38)),
            Padding = new Thickness(8, 4, 8, 4),
            Tag = item.Id
        };

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) });

        // Description
        var descBox = new TextBox
        {
            Text = item.Description,
            PlaceholderText = "Part description",
            Margin = new Thickness(2),
            Tag = "desc"
        };
        descBox.TextChanged += (s, e) => { item.Description = descBox.Text; DebounceSave(); };
        Grid.SetColumn(descBox, 0);
        rowGrid.Children.Add(descBox);

        // Part Number
        var partBox = new TextBox
        {
            Text = item.PartNumber,
            PlaceholderText = "Part #",
            Margin = new Thickness(2),
            Tag = "partnum"
        };
        partBox.TextChanged += (s, e) => { item.PartNumber = partBox.Text; DebounceSave(); };
        Grid.SetColumn(partBox, 1);
        rowGrid.Children.Add(partBox);

        // Quantity
        var qtyBox = new NumberBox
        {
            Value = item.Quantity,
            Minimum = 1,
            Maximum = 9999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Margin = new Thickness(2)
        };
        qtyBox.ValueChanged += (s, e) =>
        {
            item.Quantity = double.IsNaN(qtyBox.Value) ? 1 : (int)qtyBox.Value;
            DebounceSave();
        };
        Grid.SetColumn(qtyBox, 2);
        rowGrid.Children.Add(qtyBox);

        // Notes
        var notesBox = new TextBox
        {
            Text = item.Notes,
            PlaceholderText = "Notes",
            Margin = new Thickness(2),
            Tag = "notes"
        };
        notesBox.TextChanged += (s, e) => { item.Notes = notesBox.Text; DebounceSave(); };
        Grid.SetColumn(notesBox, 3);
        rowGrid.Children.Add(notesBox);

        // Delete button
        var delBtn = new Button
        {
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 80, 80))
            },
            Padding = new Thickness(4),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        delBtn.Click += (s, e) =>
        {
            if (_currentRequest == null) return;
            _currentRequest.Items.Remove(item);
            _partsListPanel?.Children.Remove(rowBorder);
            UpdatePartCount();
            DebounceSave();
        };
        Grid.SetColumn(delBtn, 4);
        rowGrid.Children.Add(delBtn);

        rowBorder.Child = rowGrid;
        return rowBorder;
    }

    private void LoadRequests()
    {
        // Single-sheet model: use the first saved request, or create one if none exist.
        var requests = _service.GetAllRequests();
        var request = requests.Count > 0 ? requests[0] : _service.CreateNew();
        SelectRequest(request);
    }

    private void SelectRequest(PartsRequest request)
    {
        _currentRequest = request;
        _roBox!.TextChanged -= OnRoTextChanged;
        _roBox.Text = request.RoNumber;
        _roBox.TextChanged += OnRoTextChanged;
        _dateText!.Text = $"Created: {request.CreatedDate:MM/dd/yyyy}";

        // Header / sub-header (detach-free: these boxes only write back when _currentRequest is set,
        // and we're setting it to the same request, so re-assigning is idempotent).
        if (_headerBox != null) _headerBox.Text = request.Header ?? "";
        if (_subHeaderBox != null) _subHeaderBox.Text = request.SubHeader ?? "";

        EnsureMinimumRows();
        RebuildPartsRows();
        UpdatePartCount();
        UpdateLineCountText();
    }

    private void UpdateLineCountText()
    {
        if (_lineCountText != null)
            _lineCountText.Text = (_currentRequest?.Items.Count ?? 0).ToString();
    }

    private void OnAddLines(object sender, RoutedEventArgs e) => AddBlankLines(5);
    private void OnRemoveLines(object sender, RoutedEventArgs e) => RemoveBlankLines(5);

    /// <summary>Add N blank, editable rows to the list (visible immediately + printed).</summary>
    private void AddBlankLines(int count)
    {
        if (_currentRequest == null)
        {
            _currentRequest = _service.CreateNew();
            SelectRequest(_currentRequest);
        }
        for (int i = 0; i < count; i++)
        {
            var item = new PartsRequestItem();
            _currentRequest!.Items.Add(item);
            _partsListPanel?.Children.Add(CreatePartRow(item));
        }
        UpdatePartCount();
        UpdateLineCountText();
        DebounceSave();
    }

    /// <summary>
    /// Remove up to N trailing BLANK rows. Stops at the first row that has data typed in,
    /// so this never deletes anything the user filled out.
    /// </summary>
    private void RemoveBlankLines(int count)
    {
        if (_currentRequest == null) return;

        int removed = 0;
        for (int i = _currentRequest.Items.Count - 1; i >= 0 && removed < count; i--)
        {
            var item = _currentRequest.Items[i];
            bool isEmpty = string.IsNullOrWhiteSpace(item.Description)
                        && string.IsNullOrWhiteSpace(item.PartNumber)
                        && string.IsNullOrWhiteSpace(item.Notes);
            if (!isEmpty) break; // stop at the first row with data
            _currentRequest.Items.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            RebuildPartsRows();
            UpdatePartCount();
            UpdateLineCountText();
            DebounceSave();
        }
        else
        {
            ShowNotification("No blank lines to remove — the last rows have data", InfoBarSeverity.Informational);
        }
    }

    /// <summary>Seed a full page of blank rows when a request has none, so it never opens empty.</summary>
    private void EnsureMinimumRows()
    {
        if (_currentRequest == null) return;
        if (_currentRequest.Items.Count == 0)
        {
            for (int i = 0; i < DefaultPdfLines; i++)
                _currentRequest.Items.Add(new PartsRequestItem());
        }
    }

    private void RebuildPartsRows()
    {
        if (_partsListPanel == null || _currentRequest == null) return;
        _partsListPanel.Children.Clear();
        foreach (var item in _currentRequest.Items)
        {
            _partsListPanel.Children.Add(CreatePartRow(item));
        }
    }

    private void OnRoTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_currentRequest != null)
        {
            _currentRequest.RoNumber = _roBox!.Text;
            DebounceSave();
        }
    }

    private void OnAddPart(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null)
        {
            // Auto-create a new request
            _currentRequest = _service.CreateNew();
            SelectRequest(_currentRequest);
        }

        var item = new PartsRequestItem();
        _currentRequest.Items.Add(item);
        _partsListPanel?.Children.Add(CreatePartRow(item));
        UpdatePartCount();
        DebounceSave();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null) return;
        _currentRequest.Items.Clear();
        _currentRequest.RoNumber = "";
        _roBox!.TextChanged -= OnRoTextChanged;
        _roBox.Text = "";
        _roBox.TextChanged += OnRoTextChanged;
        // Reseed a fresh page of blank rows so the form doesn't go empty.
        EnsureMinimumRows();
        RebuildPartsRows();
        UpdatePartCount();
        UpdateLineCountText();
        SaveCurrent();
        ShowNotification("Form cleared", InfoBarSeverity.Informational);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null || _currentRequest.Items.Count == 0)
        {
            ShowNotification("Add at least one part before exporting", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            SaveCurrent();
            var pdfPath = _service.GeneratePdf(_currentRequest);

            DocumentUsageTrackingService.Instance.RecordPdfExport(
                "PartsRequest", Path.GetFileName(pdfPath), _currentRequest.Items.Count);

            Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true
            });
            ShowNotification("Parts Request exported!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void DebounceSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void SaveCurrent()
    {
        if (_currentRequest == null) return;
        _currentRequest.RoNumber = _roBox?.Text ?? "";
        _service.SaveRequest(_currentRequest);
    }

    private void UpdatePartCount()
    {
        // Find the part count TextBlock in the footer
        if (Content is Grid mainGrid)
        {
            foreach (var child in mainGrid.Children)
            {
                if (child is Border border && Grid.GetRow(border as FrameworkElement) == 2
                    && border.Child is Grid footerGrid)
                {
                    foreach (var fc in footerGrid.Children)
                    {
                        if (fc is StackPanel sp)
                        {
                            foreach (var sc in sp.Children)
                            {
                                if (sc is TextBlock tb && tb.Tag as string == "partCount")
                                {
                                    // Count only filled rows — blank write-in rows aren't "parts".
                                    var count = _currentRequest?.Items.Count(it =>
                                        !string.IsNullOrWhiteSpace(it.Description) ||
                                        !string.IsNullOrWhiteSpace(it.PartNumber)) ?? 0;
                                    tb.Text = $"{count} part{(count == 1 ? "" : "s")}";
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void ShowNotification(string message, InfoBarSeverity severity)
    {
        if (_infoBar == null) return;
        _infoBar.Message = message;
        _infoBar.Severity = severity;
        _infoBar.IsOpen = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            _infoBar.IsOpen = false;
        };
        timer.Start();
    }
}
