using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Text;
using McStudDesktop.Services;
using Windows.UI;

namespace McStudDesktop.Views;

/// <summary>
/// Interactive help view that uses ExcelKnowledgeService to answer questions about the spreadsheet
/// </summary>
public class ExcelHelpView : UserControl
{
    private readonly ExcelKnowledgeService _knowledgeService;
    private TextBox _queryInput = null!;
    private StackPanel _resultsPanel = null!;
    private ListView _suggestedQuestionsView = null!;
    private ScrollViewer _resultsScroll = null!;

    // Suggested questions for quick access
    private static readonly string[] SuggestedQuestions = new[]
    {
        "Where do I enter customer information?",
        "How do I calculate refinish hours?",
        "What is the SOP List sheet for?",
        "Where are mechanical operations entered?",
        "How do I add body repair operations?",
        "Where is the cover car percentage?",
        "What inputs does Total Loss need?",
        "How do I handle SRS/airbag operations?",
        "Where are ADAS calibrations entered?",
        "What is the Stolen Recovery sheet?"
    };

    public ExcelHelpView()
    {
        _knowledgeService = new ExcelKnowledgeService();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var mainGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header
                new RowDefinition { Height = GridLength.Auto }, // Search box
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Results
                new RowDefinition { Height = GridLength.Auto } // Suggestions
            },
            Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
            Padding = new Thickness(8)
        };

        // Header
        var header = CreateHeader();
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Search box
        var searchBox = CreateSearchBox();
        Grid.SetRow(searchBox, 1);
        mainGrid.Children.Add(searchBox);

        // Results area
        var resultsArea = CreateResultsArea();
        Grid.SetRow(resultsArea, 2);
        mainGrid.Children.Add(resultsArea);

        // Suggested questions
        var suggestions = CreateSuggestionsPanel();
        Grid.SetRow(suggestions, 3);
        mainGrid.Children.Add(suggestions);

        Content = mainGrid;
    }

    private FrameworkElement CreateHeader()
    {
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Spacing = 8
        };

        var icon = new FontIcon
        {
            Glyph = "\uE946", // Help icon
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
            FontSize = 20
        };

        var title = new TextBlock
        {
            Text = "Excel Help Assistant",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = "Ask about any cell, sheet, or operation",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        headerPanel.Children.Add(icon);
        headerPanel.Children.Add(title);
        headerPanel.Children.Add(subtitle);

        return headerPanel;
    }

    private FrameworkElement CreateSearchBox()
    {
        var searchGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Margin = new Thickness(0, 0, 0, 8)
        };

        _queryInput = new TextBox
        {
            PlaceholderText = "Ask a question... (e.g., \"Where do I enter labor rate?\")",
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(4, 0, 0, 4)
        };
        _queryInput.KeyDown += OnQueryKeyDown;
        Grid.SetColumn(_queryInput, 0);

        var searchButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE721", FontSize = 14 }, // Search icon
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(0, 4, 4, 0)
        };
        searchButton.Click += OnSearchClick;
        Grid.SetColumn(searchButton, 1);

        searchGrid.Children.Add(_queryInput);
        searchGrid.Children.Add(searchButton);

        return searchGrid;
    }

    private FrameworkElement CreateResultsArea()
    {
        _resultsScroll = new ScrollViewer
        {
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 150
        };

        _resultsPanel = new StackPanel
        {
            Spacing = 8
        };

        // Welcome message
        AddWelcomeMessage();

        _resultsScroll.Content = _resultsPanel;
        return _resultsScroll;
    }

    private void AddWelcomeMessage()
    {
        _resultsPanel.Children.Clear();

        var welcomePanel = new StackPanel { Spacing = 8 };

        var welcomeText = new TextBlock
        {
            Text = "Welcome! I can help you find information in the MET Excel spreadsheet.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            TextWrapping = TextWrapping.Wrap
        };

        var helpTypes = new[]
        {
            ("📍", "Find Locations", "Ask where to enter specific data"),
            ("📋", "Explain Sheets", "Learn what each sheet is for"),
            ("🔢", "Understand Inputs", "Get help with specific inputs"),
            ("📚", "How-To Guides", "Step-by-step instructions")
        };

        welcomePanel.Children.Add(welcomeText);

        foreach (var (emoji, title, desc) in helpTypes)
        {
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 4, 0, 0)
            };

            var emojiText = new TextBlock
            {
                Text = emoji,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = title + " - ",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var descText = new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };

            itemPanel.Children.Add(emojiText);
            itemPanel.Children.Add(titleText);
            itemPanel.Children.Add(descText);
            welcomePanel.Children.Add(itemPanel);
        }

        _resultsPanel.Children.Add(welcomePanel);
    }

    private FrameworkElement CreateSuggestionsPanel()
    {
        var expander = new Expander
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE82F", // Lightbulb
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 50)),
                        FontSize = 14
                    },
                    new TextBlock
                    {
                        Text = "Suggested Questions",
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    }
                }
            },
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var suggestionsWrap = new ItemsWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 170
        };

        _suggestedQuestionsView = new ListView
        {
            ItemsSource = SuggestedQuestions,
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 120
        };
        _suggestedQuestionsView.ItemClick += OnSuggestionClick;

        // Custom item template - wrapping buttons
        var wrapPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        foreach (var question in SuggestedQuestions)
        {
            var btn = CreateSuggestionButton(question);
            wrapPanel.Children.Add(btn);
        }

        var wrapScroll = new ScrollViewer
        {
            Content = wrapPanel,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            MaxHeight = 80
        };

        expander.Content = wrapScroll;
        return expander;
    }

    private Button CreateSuggestionButton(string question)
    {
        // Truncate for display
        var displayText = question.Length > 30 ? question.Substring(0, 27) + "..." : question;

        var btn = new Button
        {
            Content = displayText,
            Tag = question,
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(3),
            FontSize = 11,
            Margin = new Thickness(0, 2, 4, 2)
        };
        btn.Click += (s, e) =>
        {
            if (s is Button b && b.Tag is string fullQuestion)
            {
                _queryInput.Text = fullQuestion;
                PerformSearch(fullQuestion);
            }
        };

        ToolTipService.SetToolTip(btn, question);
        return btn;
    }

    private void OnQueryKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PerformSearch(_queryInput.Text);
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        PerformSearch(_queryInput.Text);
    }

    private void OnSuggestionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string question)
        {
            _queryInput.Text = question;
            PerformSearch(question);
        }
    }

    private void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        _resultsPanel.Children.Clear();

        // Add the question
        AddQuestionBubble(query);

        // Try multiple query types and combine results
        var result = _knowledgeService.AnswerQuestion(query);

        if (result.Success)
        {
            AddAnswerBubble(result);
        }
        else
        {
            // Try more specific searches
            var locationResult = _knowledgeService.FindLocation(query);
            var searchResult = _knowledgeService.SearchInputs(query);

            if (locationResult.Success || searchResult.Success)
            {
                if (locationResult.Success)
                    AddAnswerBubble(locationResult);
                if (searchResult.Success && searchResult.Locations.Count > 0)
                    AddSearchResults(searchResult);
            }
            else
            {
                AddNoResultsMessage(query);
            }
        }
    }

    private void AddQuestionBubble(string question)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
            CornerRadius = new CornerRadius(12, 12, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 300,
            Margin = new Thickness(40, 0, 0, 0)
        };

        var text = new TextBlock
        {
            Text = question,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        };

        bubble.Child = text;
        _resultsPanel.Children.Add(bubble);
    }

    private void AddAnswerBubble(ExcelQueryResult result)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(12, 12, 12, 0),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 320,
            Margin = new Thickness(0, 4, 40, 0)
        };

        var content = new StackPanel { Spacing = 6 };

        // Main answer
        var answerText = new TextBlock
        {
            Text = result.Answer,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(answerText);

        // Location matches
        if (result.Locations.Count > 0)
        {
            var locationsHeader = new TextBlock
            {
                Text = "📍 Locations:",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
            content.Children.Add(locationsHeader);

            foreach (var loc in result.Locations.Take(5))
            {
                var locItem = CreateLocationItem(loc);
                content.Children.Add(locItem);
            }

            if (result.Locations.Count > 5)
            {
                var moreText = new TextBlock
                {
                    Text = $"...and {result.Locations.Count - 5} more",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                };
                content.Children.Add(moreText);
            }
        }

        bubble.Child = content;
        _resultsPanel.Children.Add(bubble);
    }

    private FrameworkElement CreateLocationItem(LocationMatch loc)
    {
        var panel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Margin = new Thickness(8, 2, 0, 2)
        };

        var labelText = new TextBlock
        {
            Text = $"• {loc.Label}",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };
        Grid.SetColumn(labelText, 0);

        var cellText = new TextBlock
        {
            Text = $"{loc.Sheet}!{loc.Cell}",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 220, 150)),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(cellText, 1);

        panel.Children.Add(labelText);
        panel.Children.Add(cellText);

        return panel;
    }

    private void AddSearchResults(ExcelQueryResult result)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(12, 12, 12, 0),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 320,
            Margin = new Thickness(0, 4, 40, 0)
        };

        var content = new StackPanel { Spacing = 4 };

        var header = new TextBlock
        {
            Text = $"🔍 Found {result.Locations.Count} matching inputs:",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        };
        content.Children.Add(header);

        foreach (var loc in result.Locations.Take(8))
        {
            var locItem = CreateLocationItem(loc);
            content.Children.Add(locItem);
        }

        if (result.Locations.Count > 8)
        {
            var moreText = new TextBlock
            {
                Text = $"...and {result.Locations.Count - 8} more",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            };
            content.Children.Add(moreText);
        }

        bubble.Child = content;
        _resultsPanel.Children.Add(bubble);
    }

    private void AddNoResultsMessage(string query)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 45, 45)),
            CornerRadius = new CornerRadius(12, 12, 12, 0),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 300,
            Margin = new Thickness(0, 4, 40, 0)
        };

        var content = new StackPanel { Spacing = 4 };

        var noResultText = new TextBlock
        {
            Text = "I couldn't find specific information for that query.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 180)),
            TextWrapping = TextWrapping.Wrap
        };

        var suggestionText = new TextBlock
        {
            Text = "Try asking about:\n• A specific sheet (e.g., 'SOP List')\n• A type of operation (e.g., 'refinish')\n• A specific input (e.g., 'labor rate')",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        content.Children.Add(noResultText);
        content.Children.Add(suggestionText);

        bubble.Child = content;
        _resultsPanel.Children.Add(bubble);
    }

    /// <summary>
    /// Get contextual help for a specific input key
    /// </summary>
    public string GetInputHelp(string inputKey)
    {
        var result = _knowledgeService.FindLocation(inputKey);
        if (result.Success && result.Locations.Count > 0)
        {
            var loc = result.Locations[0];
            return $"{loc.Label}\nSheet: {loc.Sheet}\nCell: {loc.Cell}\n{loc.Description}";
        }
        return $"No help available for '{inputKey}'";
    }

    /// <summary>
    /// Get sheet description for tooltips
    /// </summary>
    public string GetSheetDescription(string sheetName)
    {
        var result = _knowledgeService.DescribeSheet(sheetName);
        return result.Success ? result.Answer : $"No description for {sheetName}";
    }
}
