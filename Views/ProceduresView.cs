using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace McStudDesktop.Views;

public class ProceduresView : UserControl
{
    private readonly StackPanel _resultsPanel;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryFilter;
    private List<ProcedureItem> _allProcedures = new();
    private readonly HashSet<string> _categories = new() { "All Categories" };

    /// <summary>
    /// Public method to search for a specific procedure (called from navigation)
    /// </summary>
    public void SearchFor(string searchTerm)
    {
        _searchBox.Text = searchTerm;
        FilterAndDisplayProcedures();
    }

    public ProceduresView()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(16)
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header with search
        var headerPanel = new StackPanel { Spacing = 12 };

        var titleText = new TextBlock
        {
            Text = "MOTOR / Mitchell Procedures",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
        };
        headerPanel.Children.Add(titleText);

        var subtitleText = new TextBlock
        {
            Text = "Step-by-step procedures for common collision repair operations",
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        headerPanel.Children.Add(subtitleText);

        // Search and filter row
        var searchRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 16)
        };

        _searchBox = new TextBox
        {
            PlaceholderText = "Search procedures by name, category, operation type...",
            Width = 400,
            Height = 36
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        searchRow.Children.Add(_searchBox);

        _categoryFilter = new ComboBox
        {
            Width = 200,
            Height = 36,
            PlaceholderText = "Filter by Category"
        };
        _categoryFilter.SelectionChanged += OnCategoryFilterChanged;
        searchRow.Children.Add(_categoryFilter);

        headerPanel.Children.Add(searchRow);
        Grid.SetRow(headerPanel, 0);
        mainGrid.Children.Add(headerPanel);

        // Scrollable results
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _resultsPanel = new StackPanel { Spacing = 12 };
        scrollViewer.Content = _resultsPanel;
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(scrollViewer);

        Content = mainGrid;

        LoadProcedures();
    }

    private async void LoadProcedures()
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var filePath = Path.Combine(basePath, "Data", "Procedures.json");

            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("procedures", out var proceduresArray))
                {
                    foreach (var proc in proceduresArray.EnumerateArray())
                    {
                        var item = new ProcedureItem
                        {
                            Id = proc.GetProperty("id").GetString() ?? "",
                            Name = proc.GetProperty("name").GetString() ?? "",
                            Category = proc.GetProperty("category").GetString() ?? "",
                            Operation = proc.GetProperty("operation").GetString() ?? "",
                            PPageRef = proc.TryGetProperty("pPageRef", out var ppage) ? ppage.GetString() ?? "" : "",
                            Description = proc.GetProperty("description").GetString() ?? "",
                            TimeGuideline = GetTimeGuideline(proc),
                            Notes = proc.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : ""
                        };

                        // Get steps
                        if (proc.TryGetProperty("steps", out var stepsArray))
                        {
                            foreach (var step in stepsArray.EnumerateArray())
                            {
                                item.Steps.Add(step.GetString() ?? "");
                            }
                        }

                        // Get equipment
                        if (proc.TryGetProperty("equipment", out var equipArray))
                        {
                            foreach (var equip in equipArray.EnumerateArray())
                            {
                                item.Equipment.Add(equip.GetString() ?? "");
                            }
                        }

                        // Get prerequisites
                        if (proc.TryGetProperty("prerequisites", out var prereqArray))
                        {
                            foreach (var prereq in prereqArray.EnumerateArray())
                            {
                                item.Prerequisites.Add(prereq.GetString() ?? "");
                            }
                        }

                        // Get triggers
                        if (proc.TryGetProperty("triggers", out var triggerArray))
                        {
                            foreach (var trigger in triggerArray.EnumerateArray())
                            {
                                item.Triggers.Add(trigger.GetString() ?? "");
                            }
                        }

                        // Get materials
                        if (proc.TryGetProperty("materials", out var materialsArray))
                        {
                            foreach (var mat in materialsArray.EnumerateArray())
                            {
                                item.Materials.Add(mat.GetString() ?? "");
                            }
                        }

                        // Get tags
                        if (proc.TryGetProperty("tags", out var tagsArray))
                        {
                            foreach (var tag in tagsArray.EnumerateArray())
                            {
                                item.Tags.Add(tag.GetString() ?? "");
                            }
                        }

                        // Get related DEG inquiries
                        if (proc.TryGetProperty("relatedDEG", out var degArray))
                        {
                            foreach (var deg in degArray.EnumerateArray())
                            {
                                item.RelatedDEG.Add(deg.GetString() ?? "");
                            }
                        }

                        _allProcedures.Add(item);
                        _categories.Add(item.Category);
                    }
                }
            }

            // Populate category filter
            _categoryFilter.Items.Clear();
            foreach (var cat in _categories.OrderBy(c => c == "All Categories" ? "" : c))
            {
                _categoryFilter.Items.Add(cat);
            }
            _categoryFilter.SelectedIndex = 0;

            DisplayProcedures(_allProcedures);
        }
        catch (Exception ex)
        {
            var errorText = new TextBlock
            {
                Text = $"Error loading procedures: {ex.Message}",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100))
            };
            _resultsPanel.Children.Add(errorText);
        }
    }

    private string GetTimeGuideline(JsonElement proc)
    {
        if (proc.TryGetProperty("timeGuideline", out var timeVal))
        {
            if (timeVal.ValueKind == JsonValueKind.String)
            {
                return timeVal.GetString() ?? "";
            }
            else if (timeVal.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var prop in timeVal.EnumerateObject())
                {
                    parts.Add($"{prop.Name}: {prop.Value.GetString()}");
                }
                return string.Join("; ", parts);
            }
        }
        return "";
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        FilterAndDisplayProcedures();
    }

    private void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        FilterAndDisplayProcedures();
    }

    private void FilterAndDisplayProcedures()
    {
        var searchText = _searchBox.Text?.ToLower() ?? "";
        var selectedCategory = _categoryFilter.SelectedItem?.ToString() ?? "All Categories";

        var filtered = _allProcedures.Where(p =>
        {
            var matchesCategory = selectedCategory == "All Categories" || p.Category == selectedCategory;
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                p.Name.ToLower().Contains(searchText) ||
                p.Category.ToLower().Contains(searchText) ||
                p.Operation.ToLower().Contains(searchText) ||
                p.Description.ToLower().Contains(searchText) ||
                p.Tags.Any(t => t.ToLower().Contains(searchText));
            return matchesCategory && matchesSearch;
        }).ToList();

        DisplayProcedures(filtered);
    }

    private void DisplayProcedures(List<ProcedureItem> procedures)
    {
        _resultsPanel.Children.Clear();

        if (procedures.Count == 0)
        {
            var noResults = new TextBlock
            {
                Text = "No procedures found matching your search.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170)),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Margin = new Thickness(0, 20, 0, 0)
            };
            _resultsPanel.Children.Add(noResults);
            return;
        }

        var countText = new TextBlock
        {
            Text = $"Showing {procedures.Count} procedure(s)",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _resultsPanel.Children.Add(countText);

        foreach (var proc in procedures)
        {
            var card = CreateProcedureCard(proc);
            _resultsPanel.Children.Add(card);
        }
    }

    private UIElement CreateProcedureCard(ProcedureItem proc)
    {
        var expander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
            Padding = new Thickness(0)
        };

        // Header
        var headerGrid = new Grid { Margin = new Thickness(8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var headerLeft = new StackPanel();

        var nameText = new TextBlock
        {
            Text = proc.Name,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap
        };
        headerLeft.Children.Add(nameText);

        var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };

        // Category badge
        var categoryBadge = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 90)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2)
        };
        categoryBadge.Child = new TextBlock
        {
            Text = proc.Category,
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 220))
        };
        metaPanel.Children.Add(categoryBadge);

        // Operation status badge
        var isNotIncluded = proc.Operation.ToUpper().Contains("NOT INCLUDED");
        var operationBadge = new Border
        {
            Background = new SolidColorBrush(isNotIncluded
                ? Windows.UI.Color.FromArgb(255, 120, 50, 50)
                : Windows.UI.Color.FromArgb(255, 50, 100, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2)
        };
        operationBadge.Child = new TextBlock
        {
            Text = proc.Operation,
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
        };
        metaPanel.Children.Add(operationBadge);

        // P-Page reference
        if (!string.IsNullOrEmpty(proc.PPageRef))
        {
            var ppageLink = new HyperlinkButton
            {
                Content = $"P-Page: {proc.PPageRef}",
                FontSize = 11,
                Padding = new Thickness(0)
            };
            ppageLink.Click += (s, e) =>
            {
                var url = $"https://www.cccis.com/motor-guide/{proc.PPageRef.ToLower()}";
                try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url)); } catch { }
            };
            metaPanel.Children.Add(ppageLink);
        }

        headerLeft.Children.Add(metaPanel);
        Grid.SetColumn(headerLeft, 0);
        headerGrid.Children.Add(headerLeft);

        // Time guideline on right
        if (!string.IsNullOrEmpty(proc.TimeGuideline))
        {
            var timePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var timeLabel = new TextBlock
            {
                Text = "Time:",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130))
            };
            timePanel.Children.Add(timeLabel);
            var timeValue = new TextBlock
            {
                Text = proc.TimeGuideline,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255)),
                MaxWidth = 200,
                TextWrapping = TextWrapping.Wrap
            };
            timePanel.Children.Add(timeValue);
            Grid.SetColumn(timePanel, 1);
            headerGrid.Children.Add(timePanel);
        }

        expander.Header = headerGrid;

        // Content
        var contentPanel = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35))
        };

        // Description
        var descText = new TextBlock
        {
            Text = proc.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        contentPanel.Children.Add(descText);

        // Steps section
        if (proc.Steps.Count > 0)
        {
            var stepsSection = CreateSection("Procedure Steps", proc.Steps, true);
            contentPanel.Children.Add(stepsSection);
        }

        // Equipment section
        if (proc.Equipment.Count > 0)
        {
            var equipSection = CreateSection("Equipment Required", proc.Equipment, false);
            contentPanel.Children.Add(equipSection);
        }

        // Materials section
        if (proc.Materials.Count > 0)
        {
            var matSection = CreateSection("Materials", proc.Materials, false);
            contentPanel.Children.Add(matSection);
        }

        // Prerequisites section
        if (proc.Prerequisites.Count > 0)
        {
            var prereqSection = CreateSection("Prerequisites", proc.Prerequisites, false);
            contentPanel.Children.Add(prereqSection);
        }

        // Triggers section
        if (proc.Triggers.Count > 0)
        {
            var triggerSection = CreateSection("When Required", proc.Triggers, false);
            contentPanel.Children.Add(triggerSection);
        }

        // Notes
        if (!string.IsNullOrEmpty(proc.Notes))
        {
            var notesPanel = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 70)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12)
            };
            var notesText = new TextBlock
            {
                Text = $"Note: {proc.Notes}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 255)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            };
            notesPanel.Child = notesText;
            contentPanel.Children.Add(notesPanel);
        }

        // Action buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var copyButton = new Button
        {
            Content = "Copy Procedure",
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 70))
        };
        copyButton.Click += (s, e) => CopyProcedureToClipboard(proc, copyButton);
        buttonPanel.Children.Add(copyButton);

        var addToPdfButton = new Button
        {
            Content = "Add to PDF",
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 70))
        };
        addToPdfButton.Click += (s, e) => AddToPdf(proc, addToPdfButton);
        buttonPanel.Children.Add(addToPdfButton);

        contentPanel.Children.Add(buttonPanel);
        expander.Content = contentPanel;

        return expander;
    }

    private StackPanel CreateSection(string title, List<string> items, bool numbered)
    {
        var section = new StackPanel { Spacing = 4 };

        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        section.Children.Add(titleText);

        var index = 1;
        foreach (var item in items)
        {
            var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (numbered)
            {
                var numBadge = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 100)),
                    CornerRadius = new CornerRadius(10),
                    Width = 20,
                    Height = 20
                };
                numBadge.Child = new TextBlock
                {
                    Text = index.ToString(),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                };
                itemPanel.Children.Add(numBadge);
                index++;
            }
            else
            {
                var bullet = new TextBlock
                {
                    Text = "•",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 180, 100)),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                itemPanel.Children.Add(bullet);
            }

            var itemText = new TextBlock
            {
                Text = item,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            itemPanel.Children.Add(itemText);
            section.Children.Add(itemPanel);
        }

        return section;
    }

    private void CopyProcedureToClipboard(ProcedureItem proc, Button button)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"PROCEDURE: {proc.Name}");
        sb.AppendLine($"Category: {proc.Category}");
        sb.AppendLine($"Operation: {proc.Operation}");
        if (!string.IsNullOrEmpty(proc.PPageRef))
            sb.AppendLine($"P-Page Reference: {proc.PPageRef}");
        if (!string.IsNullOrEmpty(proc.TimeGuideline))
            sb.AppendLine($"Time Guideline: {proc.TimeGuideline}");
        sb.AppendLine();
        sb.AppendLine(proc.Description);
        sb.AppendLine();

        if (proc.Steps.Count > 0)
        {
            sb.AppendLine("STEPS:");
            for (int i = 0; i < proc.Steps.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {proc.Steps[i]}");
            }
            sb.AppendLine();
        }

        if (proc.Equipment.Count > 0)
        {
            sb.AppendLine("EQUIPMENT REQUIRED:");
            foreach (var eq in proc.Equipment)
                sb.AppendLine($"  • {eq}");
            sb.AppendLine();
        }

        if (proc.Materials.Count > 0)
        {
            sb.AppendLine("MATERIALS:");
            foreach (var mat in proc.Materials)
                sb.AppendLine($"  • {mat}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(proc.Notes))
        {
            sb.AppendLine($"NOTE: {proc.Notes}");
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(sb.ToString());
        Clipboard.SetContent(dataPackage);

        // Visual feedback
        var originalContent = button.Content;
        button.Content = "Copied!";
        button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 120, 50));

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (s, e) =>
        {
            button.Content = originalContent;
            button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 70));
            timer.Stop();
        };
        timer.Start();
    }

    private void AddToPdf(ProcedureItem proc, Button button)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Procedure: {proc.Name}");
        sb.AppendLine($"Category: {proc.Category} | Operation: {proc.Operation}");
        if (!string.IsNullOrEmpty(proc.PPageRef))
            sb.AppendLine($"P-Page: {proc.PPageRef}");
        if (!string.IsNullOrEmpty(proc.TimeGuideline))
            sb.AppendLine($"Time: {proc.TimeGuideline}");
        sb.AppendLine();
        sb.AppendLine(proc.Description);

        if (proc.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Steps:");
            for (int i = 0; i < proc.Steps.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {proc.Steps[i]}");
            }
        }

        var pdfItem = new PdfQueueItem
        {
            Id = $"proc-{proc.Id}",
            Term = $"Procedure: {proc.Name}",
            Category = proc.Category,
            Definition = proc.Description,
            Details = sb.ToString(),
            PPageRef = proc.PPageRef,
            Status = proc.Operation
        };

        DefinitionsView.PdfQueue.Add(pdfItem);
        DefinitionsView.RaiseItemAddedToPdfQueue(pdfItem);

        // Visual feedback
        button.Content = "Added!";
        button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 120, 50));

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (s, e) =>
        {
            button.Content = "Add to PDF";
            button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 70));
            timer.Stop();
        };
        timer.Start();
    }
}

public class ProcedureItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Operation { get; set; } = "";
    public string PPageRef { get; set; } = "";
    public string Description { get; set; } = "";
    public string TimeGuideline { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Steps { get; set; } = new();
    public List<string> Equipment { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();
    public List<string> Triggers { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> RelatedDEG { get; set; } = new();
}
