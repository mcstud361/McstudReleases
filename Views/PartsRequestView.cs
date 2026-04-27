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
    private ComboBox? _requestCombo;
    private TextBox? _roBox;
    private TextBlock? _dateText;
    private InfoBar? _infoBar;
    private DispatcherTimer? _saveTimer;
    private bool _suppressComboChange;

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

        // Request selector
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Request:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        });
        _requestCombo = new ComboBox
        {
            MinWidth = 220,
            VerticalAlignment = VerticalAlignment.Center
        };
        _requestCombo.SelectionChanged += OnRequestSelectionChanged;
        headerPanel.Children.Add(_requestCombo);

        // New request button
        var newBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 12 },
                    new TextBlock { Text = "New", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6, 12, 6)
        };
        newBtn.Click += OnNewRequest;
        headerPanel.Children.Add(newBtn);

        // Delete button
        var deleteBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                    new TextBlock { Text = "Delete", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6, 12, 6)
        };
        deleteBtn.Click += OnDeleteRequest;
        headerPanel.Children.Add(deleteBtn);

        // Separator
        headerPanel.Children.Add(new Border
        {
            Width = 1,
            Height = 24,
            Background = new SolidColorBrush(BorderColor),
            Margin = new Thickness(4, 0, 4, 0)
        });

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

        header.Child = headerPanel;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // ── Table area ──
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16)
        };

        var tableContainer = new StackPanel { Spacing = 0 };

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

        // Copy
        var copyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        copyContent.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 12, Foreground = new SolidColorBrush(Colors.White) });
        copyContent.Children.Add(new TextBlock { Text = "Copy", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) });

        var copyBtn = new Button
        {
            Content = copyContent,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            Margin = new Thickness(0, 0, 8, 0)
        };
        copyBtn.Click += OnCopy;
        Grid.SetColumn(copyBtn, 2);
        footerGrid.Children.Add(copyBtn);

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
        Grid.SetColumn(exportBtn, 3);
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
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) }); // Status
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });  // Notes
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) }); // Delete

        var labels = new[] { "Description", "Part #", "Qty", "Status", "Notes", "" };
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
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
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

        // Status
        var statusCombo = new ComboBox
        {
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                "Needed",
                "Ordered",
                "Received",
                "Backordered"
            },
            SelectedIndex = (int)item.Status
        };
        SetStatusColor(statusCombo, item.Status);
        statusCombo.SelectionChanged += (s, e) =>
        {
            if (statusCombo.SelectedIndex >= 0)
            {
                item.Status = (PartsRequestStatus)statusCombo.SelectedIndex;
                SetStatusColor(statusCombo, item.Status);
                DebounceSave();
            }
        };
        Grid.SetColumn(statusCombo, 3);
        rowGrid.Children.Add(statusCombo);

        // Notes
        var notesBox = new TextBox
        {
            Text = item.Notes,
            PlaceholderText = "Notes",
            Margin = new Thickness(2),
            Tag = "notes"
        };
        notesBox.TextChanged += (s, e) => { item.Notes = notesBox.Text; DebounceSave(); };
        Grid.SetColumn(notesBox, 4);
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
        Grid.SetColumn(delBtn, 5);
        rowGrid.Children.Add(delBtn);

        rowBorder.Child = rowGrid;
        return rowBorder;
    }

    private void SetStatusColor(ComboBox combo, PartsRequestStatus status)
    {
        var color = status switch
        {
            PartsRequestStatus.Needed => Color.FromArgb(255, 220, 60, 60),
            PartsRequestStatus.Ordered => Color.FromArgb(255, 220, 180, 40),
            PartsRequestStatus.Received => Color.FromArgb(255, 40, 180, 80),
            PartsRequestStatus.Backordered => Color.FromArgb(255, 220, 130, 40),
            _ => Color.FromArgb(255, 180, 180, 180)
        };
        combo.BorderBrush = new SolidColorBrush(color);
        combo.BorderThickness = new Thickness(0, 0, 0, 2);
    }

    private void LoadRequests()
    {
        RefreshCombo();
        var requests = _service.GetAllRequests();
        if (requests.Count > 0)
        {
            SelectRequest(requests[0]);
        }
    }

    private void RefreshCombo()
    {
        if (_requestCombo == null) return;
        _suppressComboChange = true;
        _requestCombo.Items.Clear();

        foreach (var req in _service.GetAllRequests())
        {
            var label = string.IsNullOrWhiteSpace(req.RoNumber)
                ? $"Request ({req.CreatedDate:MM/dd/yy})"
                : $"RO# {req.RoNumber} ({req.CreatedDate:MM/dd/yy})";
            _requestCombo.Items.Add(new ComboBoxItem { Content = label, Tag = req.Id });
        }

        // Select current
        if (_currentRequest != null)
        {
            for (int i = 0; i < _requestCombo.Items.Count; i++)
            {
                if (_requestCombo.Items[i] is ComboBoxItem ci && ci.Tag as string == _currentRequest.Id)
                {
                    _requestCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        _suppressComboChange = false;
    }

    private void SelectRequest(PartsRequest request)
    {
        _currentRequest = request;
        _roBox!.TextChanged -= OnRoTextChanged;
        _roBox.Text = request.RoNumber;
        _roBox.TextChanged += OnRoTextChanged;
        _dateText!.Text = $"Created: {request.CreatedDate:MM/dd/yyyy}";

        RebuildPartsRows();
        UpdatePartCount();
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

    private void OnRequestSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboChange) return;
        if (_requestCombo?.SelectedItem is ComboBoxItem ci && ci.Tag is string id)
        {
            var req = _service.GetRequest(id);
            if (req != null)
                SelectRequest(req);
        }
    }

    private void OnNewRequest(object sender, RoutedEventArgs e)
    {
        var request = _service.CreateNew();
        RefreshCombo();
        SelectRequest(request);
    }

    private async void OnDeleteRequest(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Parts Request?",
            Content = $"This will permanently delete this parts request{(string.IsNullOrWhiteSpace(_currentRequest.RoNumber) ? "" : $" for RO# {_currentRequest.RoNumber}")}.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _service.DeleteRequest(_currentRequest.Id);
            _currentRequest = null;
            _partsListPanel?.Children.Clear();

            var requests = _service.GetAllRequests();
            RefreshCombo();
            if (requests.Count > 0)
                SelectRequest(requests[0]);
            else
            {
                _roBox!.Text = "";
                _dateText!.Text = "";
                UpdatePartCount();
            }
            ShowNotification("Parts request deleted", InfoBarSeverity.Informational);
        }
    }

    private void OnAddPart(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null)
        {
            // Auto-create a new request
            _currentRequest = _service.CreateNew();
            RefreshCombo();
            SelectRequest(_currentRequest);
        }

        var item = new PartsRequestItem();
        _currentRequest.Items.Add(item);
        _partsListPanel?.Children.Add(CreatePartRow(item));
        UpdatePartCount();
        DebounceSave();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null || _currentRequest.Items.Count == 0)
        {
            ShowNotification("Add at least one part before copying", InfoBarSeverity.Warning);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Parts Request{(string.IsNullOrWhiteSpace(_currentRequest.RoNumber) ? "" : $" — RO# {_currentRequest.RoNumber}")}");
        sb.AppendLine(new string('-', 50));
        sb.AppendLine($"{"Description",-30} {"Part #",-15} {"Qty",-5} {"Status",-12}");
        sb.AppendLine(new string('-', 50));
        foreach (var item in _currentRequest.Items)
        {
            sb.AppendLine($"{item.Description,-30} {item.PartNumber,-15} {item.Quantity,-5} {item.Status,-12}");
        }

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ShowNotification("Copied to clipboard!", InfoBarSeverity.Success);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_currentRequest == null) return;
        _currentRequest.Items.Clear();
        _currentRequest.RoNumber = "";
        _roBox!.TextChanged -= OnRoTextChanged;
        _roBox.Text = "";
        _roBox.TextChanged += OnRoTextChanged;
        _partsListPanel?.Children.Clear();
        UpdatePartCount();
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
        RefreshCombo();
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
                                    var count = _currentRequest?.Items.Count ?? 0;
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
