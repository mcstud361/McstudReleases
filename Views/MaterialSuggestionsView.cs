#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Views;

/// <summary>
/// View for displaying material suggestions based on repair operations.
/// Shows consumables like drill bits, sanding discs, welding supplies, etc.
/// </summary>
public class MaterialSuggestionsView : UserControl
{
    private readonly MaterialSuggestionService _materialService;
    private StackPanel? _suggestionsPanel;
    private ComboBox? _operationCombo;
    private TextBox? _searchBox;

    public MaterialSuggestionsView()
    {
        _materialService = MaterialSuggestionService.Instance;
        BuildUI();
    }

    private void BuildUI()
    {
        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            Padding = new Thickness(15)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Operation selector
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Suggestions

        // Header
        var header = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 15) };
        header.Children.Add(new TextBlock
        {
            Text = "Material Suggestions",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Select operation type to see recommended materials and consumables",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Operation selector row
        var selectorRow = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Operation type dropdown
        var opStack = new StackPanel { Spacing = 5 };
        opStack.Children.Add(new TextBlock
        {
            Text = "Operation Type",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
        });

        _operationCombo = new ComboBox
        {
            PlaceholderText = "Select operation...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };

        // Add operation types
        var operations = new[]
        {
            "Welded Panel Replace",
            "Sectioning",
            "Structural",
            "Bolted Panel Replace",
            "Refinish",
            "Blend",
            "Body Filler",
            "Repair",
            "Aluminum",
            "Plastic Repair",
            "Bumper",
            "Glass",
            "Electrical",
            "Suspension",
            "A/C",
            "Cooling System",
            "Corrosion Protection",
            "Trim",
            "Molding"
        };

        foreach (var op in operations)
        {
            _operationCombo.Items.Add(new ComboBoxItem { Content = op, Tag = op.ToLower() });
        }

        _operationCombo.SelectionChanged += OnOperationSelected;
        opStack.Children.Add(_operationCombo);
        selectorRow.Children.Add(opStack);

        // Search box
        var searchStack = new StackPanel { Spacing = 5 };
        Grid.SetColumn(searchStack, 2);
        searchStack.Children.Add(new TextBlock
        {
            Text = "Search Materials",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
        });

        _searchBox = new TextBox
        {
            PlaceholderText = "Search by name...",
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _searchBox.TextChanged += OnSearchTextChanged;
        searchStack.Children.Add(_searchBox);
        selectorRow.Children.Add(searchStack);

        Grid.SetRow(selectorRow, 1);
        root.Children.Add(selectorRow);

        // Suggestions panel (scrollable)
        var suggestionsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 15)
        };

        _suggestionsPanel = new StackPanel { Spacing = 10 };
        suggestionsScroll.Content = _suggestionsPanel;
        Grid.SetRow(suggestionsScroll, 2);
        root.Children.Add(suggestionsScroll);

        // Show initial message
        ShowEmptyState();

        Content = root;
    }

    private void ShowEmptyState()
    {
        _suggestionsPanel?.Children.Clear();
        _suggestionsPanel?.Children.Add(new TextBlock
        {
            Text = "Select an operation type above to see recommended materials,\nor search for specific materials by name.",
            FontSize = 13,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 40, 0, 40)
        });
    }

    private void OnOperationSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_operationCombo?.SelectedItem is ComboBoxItem item && item.Tag is string operation)
        {
            var suggestions = _materialService.GetSuggestionsGroupedByCategory(operation);
            DisplaySuggestions(suggestions);
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_searchBox?.Text))
        {
            if (_operationCombo?.SelectedItem is ComboBoxItem item && item.Tag is string operation)
            {
                var suggestions = _materialService.GetSuggestionsGroupedByCategory(operation);
                DisplaySuggestions(suggestions);
            }
            else
            {
                ShowEmptyState();
            }
            return;
        }

        var searchResults = _materialService.SearchMaterials(_searchBox.Text);
        var grouped = searchResults
            .GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
        DisplaySuggestions(grouped);
    }

    private void DisplaySuggestions(Dictionary<string, List<MaterialSuggestionService.MaterialSuggestion>> groupedSuggestions)
    {
        _suggestionsPanel?.Children.Clear();

        if (groupedSuggestions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(_searchBox?.Text))
            {
                _suggestionsPanel?.Children.Add(CreateNoResultsPanel(_searchBox!.Text, term =>
                {
                    if (_searchBox != null) _searchBox.Text = term;
                    // TextChanged event fires automatically when Text is set
                }));
            }
            else
            {
                _suggestionsPanel?.Children.Add(new TextBlock
                {
                    Text = "No materials found for this selection.",
                    FontSize = 13,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                });
            }
            return;
        }

        foreach (var category in groupedSuggestions.OrderBy(kvp => kvp.Key))
        {
            var categoryCard = CreateCategoryCard(category.Key, category.Value);
            _suggestionsPanel?.Children.Add(categoryCard);
        }
    }

    private StackPanel CreateNoResultsPanel(string searchTerm, Action<string> onSuggestionClick)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(8, 16, 8, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = $"No results for '{searchTerm}'",
            FontSize = 13,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
        });
        var tryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tryRow.Children.Add(new TextBlock { Text = "Try:", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), VerticalAlignment = VerticalAlignment.Center });
        foreach (var term in new[] { "welding", "sandpaper", "primer", "adhesive" })
        {
            var btn = new Button
            {
                Content = term,
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255)),
                BorderThickness = new Thickness(0)
            };
            var captured = term;
            btn.Click += (s, e) => onSuggestionClick(captured);
            tryRow.Children.Add(btn);
        }
        panel.Children.Add(tryRow);
        return panel;
    }

    private FrameworkElement CreateCategoryCard(string categoryName, List<MaterialSuggestionService.MaterialSuggestion> materials)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var stack = new StackPanel { Spacing = 8 };

        // Category header with icon
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = GetCategoryIcon(categoryName),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = new SolidColorBrush(GetCategoryColor(categoryName)),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = categoryName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(GetCategoryColor(categoryName))
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = $"({materials.Count})",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(headerRow);

        // Materials list
        foreach (var material in materials)
        {
            var materialRow = CreateMaterialRow(material);
            stack.Children.Add(materialRow);
        }

        card.Child = stack;
        return card;
    }

    private FrameworkElement CreateMaterialRow(MaterialSuggestionService.MaterialSuggestion material)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 2)
        };

        // Name and description
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        textStack.Children.Add(new TextBlock
        {
            Text = material.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(Colors.White)
        });

        if (!string.IsNullOrEmpty(material.Description))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = material.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140))
            });
        }

        if (!string.IsNullOrEmpty(material.Notes))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = $"Note: {material.Notes}",
                FontSize = 10,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 180, 100))
            });
        }

        row.Child = textStack;
        return row;
    }

    /// <summary>
    /// Set the operation type programmatically
    /// </summary>
    public void SetOperation(string operation)
    {
        if (_operationCombo == null) return;

        foreach (ComboBoxItem item in _operationCombo.Items)
        {
            if (item.Tag is string tag && tag.Equals(operation, StringComparison.OrdinalIgnoreCase))
            {
                _operationCombo.SelectedItem = item;
                break;
            }
        }
    }

    private string GetCategoryIcon(string category)
    {
        return category switch
        {
            MaterialSuggestionService.Categories.Cutting => "\uE8C6",     // Scissors
            MaterialSuggestionService.Categories.Welding => "\uE945",     // Repair
            MaterialSuggestionService.Categories.Sanding => "\uE90F",     // Brush
            MaterialSuggestionService.Categories.Corrosion => "\uE9F9",   // Shield
            MaterialSuggestionService.Categories.Sealing => "\uE7BA",     // Repair
            MaterialSuggestionService.Categories.Masking => "\uE8D4",     // Page
            MaterialSuggestionService.Categories.Refinish => "\uE790",    // Color
            MaterialSuggestionService.Categories.Hardware => "\uE8D8",    // Wrench
            MaterialSuggestionService.Categories.Safety => "\uE7EF",      // Shield
            MaterialSuggestionService.Categories.Electrical => "\uE945",  // Lightning
            MaterialSuggestionService.Categories.Glass => "\uE737",       // Window
            MaterialSuggestionService.Categories.Structural => "\uE80F",  // Cube
            _ => "\uE7C3"                                                  // Default
        };
    }

    private Color GetCategoryColor(string category)
    {
        return category switch
        {
            MaterialSuggestionService.Categories.Cutting => Color.FromArgb(255, 255, 100, 100),    // Red
            MaterialSuggestionService.Categories.Welding => Color.FromArgb(255, 255, 180, 80),     // Orange
            MaterialSuggestionService.Categories.Sanding => Color.FromArgb(255, 200, 150, 100),    // Tan
            MaterialSuggestionService.Categories.Corrosion => Color.FromArgb(255, 100, 200, 150),  // Teal
            MaterialSuggestionService.Categories.Sealing => Color.FromArgb(255, 150, 150, 200),    // Purple
            MaterialSuggestionService.Categories.Masking => Color.FromArgb(255, 200, 200, 100),    // Yellow
            MaterialSuggestionService.Categories.Refinish => Color.FromArgb(255, 100, 180, 255),   // Blue
            MaterialSuggestionService.Categories.Hardware => Color.FromArgb(255, 180, 180, 180),   // Gray
            MaterialSuggestionService.Categories.Safety => Color.FromArgb(255, 255, 220, 100),     // Gold
            MaterialSuggestionService.Categories.Electrical => Color.FromArgb(255, 255, 255, 100), // Yellow
            MaterialSuggestionService.Categories.Glass => Color.FromArgb(255, 150, 220, 255),      // Light blue
            MaterialSuggestionService.Categories.Structural => Color.FromArgb(255, 255, 150, 150), // Pink
            _ => Color.FromArgb(255, 180, 180, 180)                                                 // Default gray
        };
    }
}
