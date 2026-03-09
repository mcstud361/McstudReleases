#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.System;
using Windows.UI;
using McStudDesktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Text.Json;
using McstudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// Chat interface for the McStud Assistant
/// ARMED UP with full operation parsing, P-Page info, DEG references, and supplement detection
/// Now includes Learned Patterns tab for searching uploaded estimate patterns
/// </summary>
public class ChatbotView : UserControl
{
    private readonly ChatbotService _chatbotService;
    private readonly SupplementDetectorService _supplementService;
    private readonly ADASMatrixService _adasService;
    private readonly TrainingExplanationService _trainingService;
    private readonly EstimatingHelperService _helperService;
    private readonly TypeItService _typeItService;
    private readonly OperationDescriptionBuilderService _descriptionBuilder;
    private readonly EstimateAIAdvisorService _advisorService;

    private StackPanel _chatHistory = null!;
    private TextBox _inputBox = null!;
    private ScrollViewer _scrollViewer = null!;
    private StackPanel _quickRepliesPanel = null!;
    private List<string> _conversationLog = new();

    // Tab navigation
    private Grid _chatContent = null!;
    private EstimateBuilderPanel? _estimateBuilderPanel;
    private GhostEstimatePanel? _ghostEstimatePanel;
    private ScreenMonitorPanel? _screenMonitorPanel;
    private Border? _chatTabButton;
    private Border? _estimateBuilderTabButton;
    private Border? _ghostTabButton;
    private Border? _screenMonitorTabButton;
    private int _selectedTab = 0;
    private Button? _subtabHelpButton;
    private Border? _chatHeaderBorder;

    // Track operations mentioned in conversation for context
    private List<EstimateOperation> _mentionedOperations = new();

    // P-Page / NOT INCLUDED data
    private Dictionary<string, JsonElement>? _notIncludedData;
    private List<DEGInquiry>? _degInquiries;

    // Last generated operations for copy/paste
    private string _lastOperationsText = "";
    private List<OperationLine> _lastOperationLines = new();

    // Chat enhancements
    private Border? _typingIndicator;
    private List<ChatMessage> _messages = new();
    private readonly string _chatHistoryPath;
    private StackPanel? _pinnedMessagesPanel;
    private List<int> _pinnedMessageIndices = new();
    private Button? _clearChatButton;

    // Collapsible categories bar
    private Border? _categoriesBar;
    private Button? _expandCategoriesButton;
    private bool _categoriesExpanded = false;

    // Event to navigate to Export tab with operations loaded
    public event EventHandler? OnNavigateToExport;

    // Static instance for cross-view messaging
    public static ChatbotView? Instance { get; private set; }

    public ChatbotView()
    {
        Instance = this;
        _chatbotService = new ChatbotService();
        _supplementService = SupplementDetectorService.Instance;
        _adasService = ADASMatrixService.Instance;
        _trainingService = TrainingExplanationService.Instance;
        _helperService = EstimatingHelperService.Instance;
        _typeItService = new TypeItService();
        _typeItService.SetSpeedLevel(4); // Insane speed
        _typeItService.BlockUserInput = true; // Safety: block input during paste
        _descriptionBuilder = OperationDescriptionBuilderService.Instance;
        _advisorService = EstimateAIAdvisorService.Instance;

        // Chat history path
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop"
        );
        Directory.CreateDirectory(appDataPath);
        _chatHistoryPath = Path.Combine(appDataPath, "chat_history.json");

        // Load P-Page and DEG data
        LoadNotIncludedData();
        LoadDEGInquiries();

        // Main container
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Tab buttons
                new RowDefinition { Height = GridLength.Auto }, // Header
                new RowDefinition { Height = GridLength.Auto }, // Collapsible categories bar
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Content (chat or quick ops)
                new RowDefinition { Height = GridLength.Auto }, // Quick replies (chat only)
                new RowDefinition { Height = GridLength.Auto }  // Input area (chat only)
            }
        };

        // Tab buttons row
        var tabPanel = CreateTabButtons();
        Grid.SetRow(tabPanel, 0);
        mainGrid.Children.Add(tabPanel);

        // Header
        _chatHeaderBorder = CreateHeader();
        Grid.SetRow(_chatHeaderBorder, 1);
        mainGrid.Children.Add(_chatHeaderBorder);

        // Collapsible categories bar
        _categoriesBar = CreateCollapsibleCategoriesBar();
        Grid.SetRow(_categoriesBar, 2);
        mainGrid.Children.Add(_categoriesBar);

        // Chat content container
        _chatContent = new Grid();

        // Chat history scroll area
        _chatHistory = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(16)
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _chatHistory,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0)
        };
        _chatContent.Children.Add(_scrollViewer);

        // Estimate Builder (hidden initially) - merged browse + cart
        _estimateBuilderPanel = new EstimateBuilderPanel { Visibility = Visibility.Collapsed };
        _estimateBuilderPanel.OnOperationsGenerated += ExcelEstimateBuilder_OnOperationsGenerated;
        _chatContent.Children.Add(_estimateBuilderPanel);

        // Ghost Estimate Comparison (hidden initially)
        _ghostEstimatePanel = new GhostEstimatePanel { Visibility = Visibility.Collapsed };
        _ghostEstimatePanel.OnOperationAccepted += GhostEstimatePanel_OnOperationAccepted;
        _chatContent.Children.Add(_ghostEstimatePanel);

        // Screen OCR Monitor (hidden initially)
        _screenMonitorPanel = new ScreenMonitorPanel { Visibility = Visibility.Collapsed };
        _screenMonitorPanel.OnFeedToChat += ScreenMonitorPanel_OnFeedToChat;
        _screenMonitorPanel.OnLoadToBuilder += ScreenMonitorPanel_OnLoadToBuilder;
        _chatContent.Children.Add(_screenMonitorPanel);

        // Floating help button for sub-tabs
        _subtabHelpButton = ContextualHelpButton.Create("chat-subtab");
        _subtabHelpButton.HorizontalAlignment = HorizontalAlignment.Right;
        _subtabHelpButton.VerticalAlignment = VerticalAlignment.Top;
        _subtabHelpButton.Margin = new Thickness(0, 8, 12, 0);
        Canvas.SetZIndex(_subtabHelpButton, 10);
        _chatContent.Children.Add(_subtabHelpButton);

        Grid.SetRow(_chatContent, 3);
        mainGrid.Children.Add(_chatContent);

        // Quick replies
        _quickRepliesPanel = CreateQuickReplies();
        Grid.SetRow(_quickRepliesPanel, 4);
        mainGrid.Children.Add(_quickRepliesPanel);

        // Input area
        var inputArea = CreateInputArea();
        Grid.SetRow(inputArea, 5);
        mainGrid.Children.Add(inputArea);

        Content = mainGrid;

        // Load existing chat history or show welcome/quick start
        if (!LoadChatHistory())
        {
            ShowQuickStartPanel();
        }
    }

    /// <summary>
    /// Shows the Quick Start panel with clickable categories to help users discover features
    /// </summary>
    private void ShowQuickStartPanel()
    {
        var quickStartContainer = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(8)
        };

        // Welcome header with hero section
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 45, 60)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var headerStack = new StackPanel { Spacing = 8 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleRow.Children.Add(new TextBlock
        {
            Text = "🛠️",
            FontSize = 28
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "McStud Estimating Assistant",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerStack.Children.Add(titleRow);

        headerStack.Children.Add(new TextBlock
        {
            Text = "Your AI-powered partner for comprehensive collision estimates. Ask questions, build operations, or explore the tabs above for specialized tools.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 190, 200)),
            TextWrapping = TextWrapping.Wrap
        });

        // Quick tip row
        var tipRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        tipRow.Children.Add(new TextBlock
        {
            Text = "💡",
            FontSize = 12
        });
        tipRow.Children.Add(new TextBlock
        {
            Text = "Pro tip: Try 'replacing front bumper 3.0 refinish' or 'what's included with quarter panel replace'",
            FontSize = 11,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 180, 220)),
            TextWrapping = TextWrapping.Wrap
        });
        headerStack.Children.Add(tipRow);

        headerBorder.Child = headerStack;
        quickStartContainer.Children.Add(headerBorder);

        // Subtitle
        quickStartContainer.Children.Add(new TextBlock
        {
            Text = "Or explore these categories:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160)),
            Margin = new Thickness(0, 4, 0, 0)
        });

        // Category cards
        var categories = new List<QuickStartCategory>
        {
            new("🎯", "The McStud Approach", "Why systematic estimating matters", new[]
            {
                "What is the McStud systematic approach?",
                "Why is consistency important?",
                "Who created McStud?"
            }),
            new("📝", "Build Operations", "Enter repair details, get formatted operation lines", new[]
            {
                "Replacing front bumper, 1.0 trial fit, 3.0 refinish",
                "Quarter panel replace 6.5 body, 4.0 refinish",
                "Hood replace 2.0 body, blend fenders"
            }),
            new("✍️", "Write Descriptions", "Turn informal notes into professional language", new[]
            {
                "fix the door dent, few dings and a crease",
                "bumper has a tear near the corner",
                "scratch on fender needs touch up"
            }),
            new("📋", "P-Pages & Included", "What's included vs not included in labor times", new[]
            {
                "What's included with bumper replace?",
                "Corrosion protection included?",
                "What about seam sealer?"
            }),
            new("🔧", "Supplements & Additionals", "Find commonly missed operations", new[]
            {
                "Check bumper replace supplement",
                "What do I need for quarter panel?",
                "Suggest fender replace additionals"
            }),
            new("📡", "ADAS & Calibrations", "Calibration requirements by repair", new[]
            {
                "ADAS for windshield replace",
                "Bumper radar calibration?",
                "What triggers camera calibration?"
            }),
            new("❓", "Definitions & Training", "Learn estimating terms and concepts", new[]
            {
                "What is flex additive?",
                "Why corrosion protection?",
                "Explain blend formula"
            }),
            new("📊", "DEG Inquiries", "Database Enhancement Gateway references", new[]
            {
                "DEG seam sealer",
                "DEG scan inquiry",
                "DEG corrosion protection"
            })
        };

        // Create 2-column grid of category cards
        var cardsGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        // Add row definitions
        for (int i = 0; i < (categories.Count + 1) / 2; i++)
        {
            cardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < categories.Count; i++)
        {
            var card = CreateQuickStartCard(categories[i]);
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            cardsGrid.Children.Add(card);
        }

        quickStartContainer.Children.Add(cardsGrid);

        // Add tip at bottom
        var tipPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 60)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var tipText = new TextBlock
        {
            Text = "Tip: Just type naturally! \"I'm replacing a bumper and need to know what else to add\"",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 220)),
            TextWrapping = TextWrapping.Wrap
        };
        tipPanel.Child = tipText;
        quickStartContainer.Children.Add(tipPanel);

        _chatHistory.Children.Add(quickStartContainer);
        ScrollToBottom();
    }

    private Border CreateQuickStartCard(QuickStartCategory category)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 50)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 65)),
            BorderThickness = new Thickness(1)
        };

        var cardContent = new StackPanel { Spacing = 8 };

        // Header with icon and title
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = category.Icon,
            FontSize = 18
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = category.Title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        cardContent.Children.Add(headerRow);

        // Description
        cardContent.Children.Add(new TextBlock
        {
            Text = category.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)),
            TextWrapping = TextWrapping.Wrap
        });

        // Example buttons
        var examplesPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var example in category.Examples.Take(3))
        {
            var exampleBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 60)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };

            var btnText = new TextBlock
            {
                Text = example.Length > 40 ? example.Substring(0, 37) + "..." : example,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 200, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            exampleBtn.Content = btnText;

            var exampleToSend = example;
            exampleBtn.Click += (s, e) =>
            {
                // Clear quick start panel
                _chatHistory.Children.Clear();
                SendMessage(exampleToSend);
            };

            // Hover effect
            exampleBtn.PointerEntered += (s, e) =>
            {
                exampleBtn.Background = new SolidColorBrush(Color.FromArgb(255, 70, 80, 100));
            };
            exampleBtn.PointerExited += (s, e) =>
            {
                exampleBtn.Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 60));
            };

            examplesPanel.Children.Add(exampleBtn);
        }
        cardContent.Children.Add(examplesPanel);

        card.Child = cardContent;

        // Card hover effect
        card.PointerEntered += (s, e) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 65));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 100, 140));
        };
        card.PointerExited += (s, e) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 50));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 65));
        };

        return card;
    }

    /// <summary>
    /// Creates the collapsible categories bar that can be expanded while chatting
    /// </summary>
    private Border CreateCollapsibleCategoriesBar()
    {
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 40, 50)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 60, 70)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Visibility = Visibility.Collapsed // Hidden by default
        };

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 180
        };

        var categoriesPanel = new StackPanel { Spacing = 8 };

        // Quick categories as compact chips
        var categories = new List<(string Icon, string Label, string[] Examples)>
        {
            ("🏦", "Retirement Fund", new[] { "Retirement Fund Finder", "How much money am I leaving?", "Show my yearly ROI potential" }),
            ("🎯", "McStud Approach", new[] { "What is the McStud systematic approach?", "Why is consistency important?" }),
            ("📝", "Build Operations", new[] { "Replacing front bumper, 1.0 trial fit, 3.0 refinish", "Quarter panel replace 6.5 body" }),
            ("✍️", "Write Descriptions", new[] { "fix the door dent, few dings and a crease", "bumper has a tear near the corner" }),
            ("📋", "P-Pages/Included", new[] { "What's included with bumper replace?", "Corrosion protection included?" }),
            ("🔧", "Supplements", new[] { "Check bumper replace supplement", "Suggest fender replace additionals" }),
            ("📡", "ADAS/Calibrations", new[] { "ADAS for windshield replace", "Bumper radar calibration?" }),
            ("❓", "Definitions", new[] { "What is flex additive?", "Explain blend formula" }),
            ("📊", "DEG Inquiries", new[] { "DEG seam sealer", "DEG scan inquiry" })
        };

        // Create chip rows
        var chipsWrapPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var cat in categories)
        {
            var chip = CreateCategoryChip(cat.Icon, cat.Label, cat.Examples);
            chipsWrapPanel.Children.Add(chip);
        }
        categoriesPanel.Children.Add(chipsWrapPanel);

        scrollViewer.Content = categoriesPanel;
        container.Child = scrollViewer;

        return container;
    }

    /// <summary>
    /// Creates a compact category chip with a dropdown of examples
    /// </summary>
    private Border CreateCategoryChip(string icon, string label, string[] examples)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 80, 90)),
            BorderThickness = new Thickness(1)
        };

        var menuFlyout = new MenuFlyout();
        foreach (var example in examples)
        {
            var item = new MenuFlyoutItem
            {
                Text = example.Length > 50 ? example.Substring(0, 47) + "..." : example
            };
            var exampleToSend = example;
            item.Click += (s, e) =>
            {
                SendMessage(exampleToSend);
            };
            menuFlyout.Items.Add(item);
        }

        var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        chipContent.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        chipContent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 210, 230)),
            VerticalAlignment = VerticalAlignment.Center
        });
        chipContent.Children.Add(new FontIcon
        {
            Glyph = "\uE70D", // ChevronDown
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 150, 170)),
            VerticalAlignment = VerticalAlignment.Center
        });

        chip.Child = chipContent;

        // Click to show flyout
        chip.PointerPressed += (s, e) =>
        {
            menuFlyout.ShowAt(chip);
        };

        // Hover effect
        chip.PointerEntered += (s, e) =>
        {
            chip.Background = new SolidColorBrush(Color.FromArgb(255, 65, 75, 95));
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 100, 120, 160));
        };
        chip.PointerExited += (s, e) =>
        {
            chip.Background = new SolidColorBrush(Color.FromArgb(255, 50, 55, 65));
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 80, 90));
        };

        return chip;
    }

    /// <summary>
    /// Toggles the visibility of the collapsible categories bar
    /// </summary>
    private void ToggleCategoriesBar()
    {
        if (_categoriesBar == null || _expandCategoriesButton == null) return;

        _categoriesExpanded = !_categoriesExpanded;

        if (_categoriesExpanded)
        {
            _categoriesBar.Visibility = Visibility.Visible;
            // Change icon to chevron up
            _expandCategoriesButton.Content = new FontIcon { Glyph = "\uE70E", FontSize = 14 }; // ChevronUp
            ToolTipService.SetToolTip(_expandCategoriesButton, "Hide quick categories");
        }
        else
        {
            _categoriesBar.Visibility = Visibility.Collapsed;
            // Change icon to chevron down
            _expandCategoriesButton.Content = new FontIcon { Glyph = "\uE70D", FontSize = 14 }; // ChevronDown
            ToolTipService.SetToolTip(_expandCategoriesButton, "Show quick categories");
        }
    }

    #region Chat History Persistence

    private bool LoadChatHistory()
    {
        try
        {
            if (!File.Exists(_chatHistoryPath))
                return false;

            var json = File.ReadAllText(_chatHistoryPath);
            var history = JsonSerializer.Deserialize<ChatHistoryData>(json);

            if (history?.Messages == null || history.Messages.Count == 0)
                return false;

            // Only load if history is from today (fresh start each day)
            if (history.LastSaved.Date != DateTime.Today)
            {
                // Archive old history
                File.Delete(_chatHistoryPath);
                return false;
            }

            _messages = history.Messages;
            _pinnedMessageIndices = history.PinnedIndices ?? new List<int>();

            // Rebuild UI from loaded messages
            foreach (var msg in _messages)
            {
                var bubble = CreateMessageBubbleWithTimestamp(msg.Content, msg.IsUser, msg.Timestamp);
                _chatHistory.Children.Add(bubble);
                _conversationLog.Add($"{(msg.IsUser ? "YOU" : "ASSISTANT")}: {msg.Content}");
            }

            ScrollToBottom();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Chat] Load history error: {ex.Message}");
            return false;
        }
    }

    private void SaveChatHistory()
    {
        try
        {
            var history = new ChatHistoryData
            {
                Messages = _messages,
                PinnedIndices = _pinnedMessageIndices,
                LastSaved = DateTime.Now
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(history, options);
            File.WriteAllText(_chatHistoryPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Chat] Save history error: {ex.Message}");
        }
    }

    private async void ClearChatHistory()
    {
        var dialog = new ContentDialog
        {
            Title = "Clear Chat History",
            Content = "This will clear all messages. Continue?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _chatHistory.Children.Clear();
            _messages.Clear();
            _conversationLog.Clear();
            _pinnedMessageIndices.Clear();
            _mentionedOperations.Clear();

            try { File.Delete(_chatHistoryPath); } catch { }

            // Show quick start panel with all categories
            ShowQuickStartPanel();
        }
    }

    #endregion

    private void LoadNotIncludedData()
    {
        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Data", "NotIncludedOperations.json");
            if (File.Exists(dataPath))
            {
                var json = File.ReadAllText(dataPath);
                var doc = JsonDocument.Parse(json);
                _notIncludedData = doc.RootElement.GetProperty("categories")
                    .EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.Clone());
            }
        }
        catch { /* Non-critical */ }
    }

    private void LoadDEGInquiries()
    {
        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Data", "DEGInquiries.json");
            if (File.Exists(dataPath))
            {
                var json = File.ReadAllText(dataPath);
                var doc = JsonDocument.Parse(json);
                _degInquiries = new List<DEGInquiry>();

                foreach (var inquiry in doc.RootElement.GetProperty("inquiries").EnumerateArray())
                {
                    _degInquiries.Add(new DEGInquiry
                    {
                        InquiryNumber = inquiry.GetProperty("inquiryNumber").GetString() ?? "",
                        Title = inquiry.GetProperty("title").GetString() ?? "",
                        Category = inquiry.GetProperty("category").GetString() ?? "",
                        Response = inquiry.GetProperty("response").GetString() ?? "",
                        KeyPoints = inquiry.GetProperty("keyPoints").EnumerateArray()
                            .Select(k => k.GetString() ?? "").ToList()
                    });
                }
            }
        }
        catch { /* Non-critical */ }
    }

    private StackPanel CreateTabButtons()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
            Padding = new Thickness(8, 8, 8, 0)
        };

        // Tab labels
        _chatTabButton = CreateTabButton("\uD83D\uDCAC Chat", "Ask questions", 0, true);
        _estimateBuilderTabButton = CreateTabButton("\uD83D\uDCCB Estimate Builder", "Browse & build", 1, false);
        _ghostTabButton = CreateTabButton("\uD83D\uDC7B Ghost Compare", "AI training", 2, false);
        _screenMonitorTabButton = CreateTabButton("\uD83D\uDDA5 Screen OCR", "Monitor screen", 3, false);

        panel.Children.Add(_chatTabButton);
        panel.Children.Add(_estimateBuilderTabButton);
        panel.Children.Add(_ghostTabButton);
        panel.Children.Add(_screenMonitorTabButton);

        return panel;
    }

    private Border CreateTabButton(string text, string subtitle, int index, bool isSelected)
    {
        // Container with active indicator
        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) }); // Active indicator

        var border = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 45, 50, 55))
                : new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(14, 10, 14, 8),
            Tag = index
        };

        var stack = new StackPanel { Spacing = 2 };

        // Main label
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(255, 140, 140, 140))
        });

        // Subtitle (only show on selected or hover)
        var subtitleBlock = new TextBlock
        {
            Text = subtitle,
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed,
            Name = "Subtitle"
        };
        stack.Children.Add(subtitleBlock);

        border.Child = stack;
        Grid.SetRow(border, 0);
        outerGrid.Children.Add(border);

        // Active indicator line
        var indicator = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 150, 200))
                : new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(2, 2, 0, 0),
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Name = "Indicator"
        };
        Grid.SetRow(indicator, 1);
        outerGrid.Children.Add(indicator);

        var resultBorder = new Border
        {
            Child = outerGrid,
            Margin = new Thickness(0, 0, 2, 0),
            Tag = index
        };

        // Hover effects
        resultBorder.PointerEntered += (s, e) =>
        {
            if (_selectedTab != index)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(255, 38, 42, 48));
                subtitleBlock.Visibility = Visibility.Visible;
            }
        };
        resultBorder.PointerExited += (s, e) =>
        {
            if (_selectedTab != index)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28));
                subtitleBlock.Visibility = Visibility.Collapsed;
            }
        };

        resultBorder.PointerPressed += (s, e) => SelectTab(index);

        return resultBorder;
    }

    private void SelectTab(int index)
    {
        _selectedTab = index;

        // Update tab button styles
        UpdateTabButtonStyle(_chatTabButton, index == 0);
        UpdateTabButtonStyle(_estimateBuilderTabButton, index == 1);
        UpdateTabButtonStyle(_ghostTabButton, index == 2);
        UpdateTabButtonStyle(_screenMonitorTabButton, index == 3);

        // Show/hide header + categories bar (only needed for chat tab)
        bool isChatTab = index == 0;
        if (_chatHeaderBorder != null)
            _chatHeaderBorder.Visibility = isChatTab ? Visibility.Visible : Visibility.Collapsed;
        if (_categoriesBar != null && !isChatTab)
            _categoriesBar.Visibility = Visibility.Collapsed;

        // Show/hide content
        if (index == 0)
        {
            // Chat tab
            _scrollViewer.Visibility = Visibility.Visible;
            _estimateBuilderPanel!.Visibility = Visibility.Collapsed;
            _ghostEstimatePanel!.Visibility = Visibility.Collapsed;
            _screenMonitorPanel!.Visibility = Visibility.Collapsed;
            _quickRepliesPanel.Visibility = Visibility.Visible;
            _inputBox.Visibility = Visibility.Visible;
        }
        else if (index == 1)
        {
            // Estimate Builder tab
            _scrollViewer.Visibility = Visibility.Collapsed;
            _estimateBuilderPanel!.Visibility = Visibility.Visible;
            _ghostEstimatePanel!.Visibility = Visibility.Collapsed;
            _screenMonitorPanel!.Visibility = Visibility.Collapsed;
            _quickRepliesPanel.Visibility = Visibility.Collapsed;
            _inputBox.Visibility = Visibility.Collapsed;
        }
        else if (index == 2)
        {
            // Ghost Estimate Comparison tab
            _scrollViewer.Visibility = Visibility.Collapsed;
            _estimateBuilderPanel!.Visibility = Visibility.Collapsed;
            _ghostEstimatePanel!.Visibility = Visibility.Visible;
            _screenMonitorPanel!.Visibility = Visibility.Collapsed;
            _quickRepliesPanel.Visibility = Visibility.Collapsed;
            _inputBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Screen OCR Monitor tab
            _scrollViewer.Visibility = Visibility.Collapsed;
            _estimateBuilderPanel!.Visibility = Visibility.Collapsed;
            _ghostEstimatePanel!.Visibility = Visibility.Collapsed;
            _screenMonitorPanel!.Visibility = Visibility.Visible;
            _quickRepliesPanel.Visibility = Visibility.Collapsed;
            _inputBox.Visibility = Visibility.Collapsed;
        }

        // Update help button for active sub-tab
        if (_subtabHelpButton != null)
        {
            var viewId = index switch
            {
                0 => "chat-subtab",
                1 => "estimate-builder-subtab",
                2 => "ghost-compare-subtab",
                3 => "screen-ocr-subtab",
                _ => "chat-subtab"
            };
            var flyout = new Flyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
            };
            flyout.Content = new ContextualHelpPanel(viewId) { MinWidth = 250, MaxWidth = 300 };
            _subtabHelpButton.Flyout = flyout;
        }
    }

    private void UpdateTabButtonStyle(Border? button, bool isSelected)
    {
        if (button == null) return;

        // New structure: Border > Grid > [Border (content), Border (indicator)]
        if (button.Child is Grid outerGrid && outerGrid.Children.Count >= 2)
        {
            // Content border
            if (outerGrid.Children[0] is Border contentBorder)
            {
                contentBorder.Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(255, 45, 50, 55))
                    : new SolidColorBrush(Color.FromArgb(255, 28, 28, 28));

                if (contentBorder.Child is StackPanel stack)
                {
                    foreach (var child in stack.Children)
                    {
                        if (child is TextBlock text)
                        {
                            if (text.Name == "Subtitle")
                            {
                                text.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                            }
                            else
                            {
                                text.Foreground = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(255, 140, 140, 140));
                                text.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                            }
                        }
                    }
                }
            }

            // Indicator border
            if (outerGrid.Children[1] is Border indicator)
            {
                indicator.Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 150, 200))
                    : new SolidColorBrush(Colors.Transparent);
            }
        }
    }

    private Border CreateHeader()
    {
        var headerGrid = new Grid
        {
            Padding = new Thickness(16, 12, 16, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Bot icon
        var iconBorder = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconText = new TextBlock
        {
            Text = "M",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        // Title
        var titleStack = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        titleStack.Children.Add(new TextBlock
        {
            Text = "McStud Assistant",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });

        titleStack.Children.Add(new TextBlock
        {
            Text = "Supplements, ADAS, Training & More",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
        });

        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        // Button stack for header actions
        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Expand categories button
        _expandCategoriesButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 14 }, // ChevronDown icon
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 80, 100)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 255)),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(_expandCategoriesButton, "Show/hide quick categories");
        _expandCategoriesButton.Click += (s, e) => ToggleCategoriesBar();
        buttonStack.Children.Add(_expandCategoriesButton);

        // Clear Chat button
        _clearChatButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 }, // Trash icon
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 80, 50, 50)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 150, 150)),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(_clearChatButton, "Clear chat history");
        _clearChatButton.Click += (s, e) => ClearChatHistory();
        buttonStack.Children.Add(_clearChatButton);

        // Copy All button
        var copyButton = new Button
        {
            Content = "Copy All",
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        copyButton.Click += CopyAllChat_Click;
        buttonStack.Children.Add(copyButton);

        Grid.SetColumn(buttonStack, 2);
        headerGrid.Children.Add(buttonStack);

        return new Border
        {
            Child = headerGrid,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private async void CopyAllChat_Click(object sender, RoutedEventArgs e)
    {
        if (_conversationLog.Count == 0)
            return;

        var fullText = string.Join("\n\n---\n\n", _conversationLog);
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(fullText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        if (sender is Button btn)
        {
            var originalContent = btn.Content;
            btn.Content = "Copied!";
            await System.Threading.Tasks.Task.Delay(1500);
            btn.Content = originalContent;
        }
    }

    private StackPanel CreateQuickReplies()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35))
        };

        UpdateQuickReplies(panel);
        return panel;
    }

    private void UpdateQuickReplies(StackPanel? panel = null)
    {
        panel ??= _quickRepliesPanel;
        if (panel == null) return;

        panel.Children.Clear();

        // Context-aware quick replies based on recent operations
        var quickReplies = GetContextualQuickReplies();

        foreach (var reply in quickReplies)
        {
            var button = new Button
            {
                Content = reply,
                FontSize = 11,
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
                CornerRadius = new CornerRadius(12)
            };
            button.Click += (s, e) => SendMessage(reply);
            panel.Children.Add(button);
        }
    }

    private List<string> GetContextualQuickReplies()
    {
        var replies = new List<string>();

        // If we have mentioned operations, suggest relevant follow-ups
        if (_mentionedOperations.Any())
        {
            var lastOp = _mentionedOperations.Last();
            var partLower = lastOp.PartName?.ToLower() ?? "";

            // Part-specific suggestions
            if (partLower.Contains("bumper"))
            {
                replies.Add("ADAS radar calibration?");
                replies.Add("Flex additive needed?");
                replies.Add("Adjacent panel blend");
            }
            else if (partLower.Contains("quarter"))
            {
                replies.Add("Corrosion protection");
                replies.Add("Seam sealer details");
                replies.Add("Blind spot calibration?");
            }
            else if (partLower.Contains("windshield") || partLower.Contains("glass"))
            {
                replies.Add("Camera calibration cost");
                replies.Add("Molding kit needed?");
            }
            else if (partLower.Contains("door"))
            {
                replies.Add("Door trim R&I");
                replies.Add("Mirror transfer");
                replies.Add("Adjacent blend panels");
            }
            else if (partLower.Contains("hood"))
            {
                replies.Add("Blend fenders?");
                replies.Add("Insulator transfer");
            }

            replies.Add("What else is NOT INCLUDED?");
        }
        else
        {
            // Default suggestions
            replies.Add("Check bumper replace");
            replies.Add("Quarter panel supplements");
            replies.Add("ADAS windshield");
            replies.Add("What's NOT INCLUDED?");
            replies.Add("3-stage paint");
        }

        return replies.Take(5).ToList();
    }

    private Grid CreateInputArea()
    {
        var inputGrid = new Grid
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        _inputBox = new TextBox
        {
            PlaceholderText = "e.g., \"Check front bumper replace\" or \"Why flex additive?\"",
            FontSize = 14,
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 70, 70)),
            CornerRadius = new CornerRadius(20)
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        Grid.SetColumn(_inputBox, 0);
        inputGrid.Children.Add(_inputBox);

        var sendButton = new Button
        {
            Content = "Send",
            FontSize = 14,
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(20)
        };
        sendButton.Click += SendButton_Click;
        Grid.SetColumn(sendButton, 1);
        inputGrid.Children.Add(sendButton);

        return inputGrid;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !string.IsNullOrWhiteSpace(_inputBox.Text))
        {
            SendMessage(_inputBox.Text);
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_inputBox.Text))
        {
            SendMessage(_inputBox.Text);
        }
    }

    private async void SendMessage(string message)
    {
        AddUserMessage(message);
        _inputBox.Text = "";

        // Show typing indicator
        ShowTypingIndicator();

        // Small delay to simulate processing
        await System.Threading.Tasks.Task.Delay(150);

        // Hide typing indicator
        HideTypingIndicator();

        // Process message and determine response type
        var response = ProcessMessage(message);

        if (response.IsAdvisorQuery)
        {
            // AI Advisor mode - unified intelligence from all learning services
            var advisorResult = _advisorService.ProcessAdvisorQuery(message);
            if (advisorResult != null)
            {
                AddAdvisorResponse(advisorResult);
            }
            else
            {
                var chatResponse = _chatbotService.GetResponse(message);
                AddBotMessage(chatResponse.Message, chatResponse.RelatedTopics);
            }
        }
        else if (response.IsDescriptionBuilderRequest)
        {
            // Description builder mode - professional descriptions from informal input
            AddDescriptionBuilderResponse(response);
        }
        else if (response.IsSuggestionRequest)
        {
            // Smart suggestions mode - show beginner help
            AddSuggestionResponse(response);
        }
        else if (response.IsFullOperationEntry)
        {
            // ARMED UP mode - full operation analysis
            AddFullOperationResponse(response);
        }
        else if (response.IsSupplementCheck)
        {
            AddSupplementResponse(response);
        }
        else if (response.IsADASCheck)
        {
            AddADASResponse(response);
        }
        else if (response.IsTrainingQuestion)
        {
            AddTrainingResponse(response);
        }
        else
        {
            // Fall back to regular chatbot
            var chatResponse = _chatbotService.GetResponse(message);
            AddBotMessage(chatResponse.Message, chatResponse.RelatedTopics);
        }
    }

    /// <summary>
    /// ARMED UP response - full operation breakdown with P-Pages, DEG, additionals
    /// </summary>
    private void AddFullOperationResponse(ProcessedMessage processed)
    {
        var part = processed.DetectedPart;
        var opType = processed.DetectedOperation;
        var parsedOps = processed.ParsedOperations;

        // Build operation lines for CCC paste format
        _lastOperationLines = BuildOperationLines(part, opType, parsedOps);
        _lastOperationsText = FormatOperationsForClipboard(_lastOperationLines);

        // Calculate totals
        var totalBodyHours = parsedOps.Where(o =>
            o.OperationType is "Body Labor" or "Repair" or "R&I" or "Trial Fit" or "Overhaul" or "Frame/Structural")
            .Sum(o => o.Hours);
        var totalRefinishHours = parsedOps.Where(o =>
            o.OperationType is "Refinish" or "Blend" or "Clear Coat")
            .Sum(o => o.Hours);

        // Track operation for context
        var operation = new EstimateOperation
        {
            OperationType = opType,
            PartName = part,
            Description = $"{opType} {part}"
        };
        _mentionedOperations.Add(operation);

        // ═══════════════════════════════════════════════════════════════
        // SECTION 1: OPERATIONS (Primary response)
        // ═══════════════════════════════════════════════════════════════
        var opsResponse = new System.Text.StringBuilder();
        opsResponse.AppendLine($"📝 OPERATIONS: {opType} {part}");
        opsResponse.AppendLine("═══════════════════════════════════════════");
        opsResponse.AppendLine();

        // Main operation line
        opsResponse.AppendLine($"  {part}");
        opsResponse.AppendLine($"  └─ {opType}");

        // Sub-operations with hours
        if (parsedOps.Any())
        {
            foreach (var op in parsedOps)
            {
                opsResponse.AppendLine($"       • {op.OperationType}: {op.Hours:N1} hr");
            }
        }

        // Totals
        opsResponse.AppendLine();
        if (totalBodyHours > 0) opsResponse.AppendLine($"  Body Labor:    {totalBodyHours:N1} hr");
        if (totalRefinishHours > 0) opsResponse.AppendLine($"  Refinish:      {totalRefinishHours:N1} hr");
        opsResponse.AppendLine($"  ─────────────────────");
        opsResponse.AppendLine($"  TOTAL:         {totalBodyHours + totalRefinishHours:N1} hr");

        // Add operations message with buttons
        AddBotMessageWithOperationButtons(opsResponse.ToString(), null);

        // ═══════════════════════════════════════════════════════════════
        // SECTION 2: P-PAGE INFO (Separate message)
        // ═══════════════════════════════════════════════════════════════
        var pPageResponse = new System.Text.StringBuilder();
        pPageResponse.AppendLine("📋 P-PAGE INFO");
        pPageResponse.AppendLine("───────────────────────────────────────────");

        var (included, notIncluded) = GetPPageInfo(part, opType, parsedOps);

        pPageResponse.AppendLine("✅ INCLUDED in labor time:");
        foreach (var item in included.Take(5))
        {
            pPageResponse.AppendLine($"   • {item}");
        }

        pPageResponse.AppendLine();
        pPageResponse.AppendLine("❌ NOT INCLUDED (add separately):");
        foreach (var item in notIncluded.Take(8))
        {
            pPageResponse.AppendLine($"   • {item}");
        }

        AddBotMessage(pPageResponse.ToString());

        // ═══════════════════════════════════════════════════════════════
        // SECTION 3: ADDITIONALS + ADAS + BLEND (Separate message)
        // ═══════════════════════════════════════════════════════════════
        var additionalsResponse = new System.Text.StringBuilder();
        additionalsResponse.AppendLine("🔧 ADDITIONAL OPERATIONS");
        additionalsResponse.AppendLine("───────────────────────────────────────────");

        var additionals = GetAssociatedAdditionals(part, opType, parsedOps);
        if (additionals.Any())
        {
            foreach (var add in additionals.Take(12))
            {
                var timeStr = add.Hours > 0 ? $"{add.Hours:N1} hr" : "";
                var costStr = add.Cost > 0 ? $"${add.Cost:N0}" : "";
                var details = string.Join("  ", new[] { timeStr, costStr }.Where(s => !string.IsNullOrEmpty(s)));
                additionalsResponse.AppendLine($"   • {add.Item,-35} {details}");
            }
        }

        // ADAS
        var adasResult = _adasService.AnalyzeForADAS(new List<EstimateOperation> { operation });
        if (adasResult.RequiredCalibrations.Any())
        {
            additionalsResponse.AppendLine();
            additionalsResponse.AppendLine("🎯 ADAS CALIBRATIONS:");
            foreach (var cal in adasResult.RequiredCalibrations.Take(3))
            {
                additionalsResponse.AppendLine($"   • {cal.SensorName} ({cal.CalibrationType}) ${cal.EstimatedCost:N0}");
            }
        }

        // Blend panels
        if (parsedOps.Any(o => o.OperationType == "Refinish") || opType.Contains("Replace"))
        {
            var blendResult = _helperService.CalculateBlendPanels(new List<string> { part });
            if (blendResult.BlendPanels.Any())
            {
                additionalsResponse.AppendLine();
                additionalsResponse.AppendLine("🎨 BLEND PANELS:");
                additionalsResponse.AppendLine($"   Consider: {string.Join(", ", blendResult.BlendPanels)}");
                additionalsResponse.AppendLine($"   Est time: {blendResult.EstimatedBlendHours:N1} hr");
            }
        }

        AddBotMessage(additionalsResponse.ToString());

        // ═══════════════════════════════════════════════════════════════
        // SECTION 4: DEG REFERENCES (Separate message, if any)
        // ═══════════════════════════════════════════════════════════════
        var relevantDEG = GetRelevantDEGInquiries(part, opType);
        if (relevantDEG.Any())
        {
            var degResponse = new System.Text.StringBuilder();
            degResponse.AppendLine("📚 DEG INQUIRY REFERENCES");
            degResponse.AppendLine("───────────────────────────────────────────");

            foreach (var deg in relevantDEG.Take(3))
            {
                degResponse.AppendLine($"DEG #{deg.InquiryNumber}");
                degResponse.AppendLine($"   {deg.Title}");
                degResponse.AppendLine($"   → {deg.KeyPoints.FirstOrDefault() ?? deg.Response}");
                degResponse.AppendLine();
            }

            var related = new List<string> { "What's NOT INCLUDED?", "Why flex additive?", "Why corrosion protection?" };
            AddBotMessage(degResponse.ToString(), related);
        }

        // ═══════════════════════════════════════════════════════════════
        // SECTION 5: AI ADVISOR PROACTIVE SUGGESTIONS
        // ═══════════════════════════════════════════════════════════════
        try
        {
            var enteredOpTypes = parsedOps.Select(o => o.OperationType).ToList();
            var proactive = _advisorService.GetProactiveSuggestions(part, opType, enteredOpTypes, null);
            if (proactive != null)
            {
                var advisorSb = new System.Text.StringBuilder();
                advisorSb.AppendLine("🤖 AI ADVISOR");
                advisorSb.AppendLine("───────────────────────────────────────────");

                if (proactive.PatternSuggestions.Any())
                {
                    advisorSb.AppendLine("📋 COMMONLY MISSED:");
                    foreach (var sug in proactive.PatternSuggestions)
                    {
                        advisorSb.AppendLine($"   • {sug.Description} ({sug.Hours:N1} hr, used {sug.TimesUsed}x)");
                    }
                    advisorSb.AppendLine();
                }

                if (!string.IsNullOrEmpty(proactive.AccuracyWarning))
                {
                    advisorSb.AppendLine($"⚠️ {proactive.AccuracyWarning}");
                    advisorSb.AppendLine();
                }

                if (!string.IsNullOrEmpty(proactive.InsurerNote))
                {
                    advisorSb.AppendLine($"💰 {proactive.InsurerNote}");
                }

                var advisorRelated = new List<string> { "What am I missing?", "Show similar estimates", "Review my estimate" };
                AddBotMessage(advisorSb.ToString(), advisorRelated);

                // Track this operation in the advisor session
                var totalHours = parsedOps.Sum(o => o.Hours);
                _advisorService.TrackEnteredOperation(part, opType, totalHours);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Advisor] Proactive suggestion error: {ex.Message}");
        }
    }

    /// <summary>
    /// Renders a multi-section AI Advisor response with dark theme cards
    /// </summary>
    private void AddAdvisorResponse(AdvisorResponse advisorResult)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🤖 AI ADVISOR");
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine();

        foreach (var section in advisorResult.Sections)
        {
            sb.AppendLine($"{section.Icon} {section.Title}");
            sb.AppendLine("───────────────────────────────────────────");
            foreach (var item in section.Items)
            {
                var hoursStr = item.Hours > 0 ? $" ({item.Hours:N1} hr)" : "";
                sb.AppendLine($"   • {item.Description}{hoursStr}");
                if (!string.IsNullOrEmpty(item.Detail))
                    sb.AppendLine($"     {item.Detail}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(advisorResult.Summary))
        {
            sb.AppendLine($"📊 {advisorResult.Summary}");
            sb.AppendLine();
        }

        AddBotMessage(sb.ToString(), advisorResult.FollowUpQuestions);
    }

    /// <summary>
    /// Build operation lines from parsed input
    /// </summary>
    private List<OperationLine> BuildOperationLines(string part, string opType, List<ParsedOperation> parsedOps)
    {
        var lines = new List<OperationLine>();

        // Main part line
        lines.Add(new OperationLine
        {
            LineType = "Part",
            Description = part,
            Operation = opType,
            Hours = 0
        });

        // Body labor operations
        var bodyOps = parsedOps.Where(o =>
            o.OperationType is "Body Labor" or "Repair" or "R&I" or "Trial Fit" or "Overhaul" or "Frame/Structural").ToList();

        foreach (var op in bodyOps)
        {
            lines.Add(new OperationLine
            {
                LineType = "Labor",
                Description = $"  {op.OperationType}",
                Operation = "Body",
                Hours = op.Hours
            });
        }

        // Refinish operations
        var refinishOps = parsedOps.Where(o =>
            o.OperationType is "Refinish" or "Blend" or "Clear Coat").ToList();

        foreach (var op in refinishOps)
        {
            lines.Add(new OperationLine
            {
                LineType = "Refinish",
                Description = $"  {op.OperationType}",
                Operation = "Refinish",
                Hours = op.Hours
            });
        }

        return lines;
    }

    /// <summary>
    /// Format operations for clipboard (tab-separated for CCC paste)
    /// Format: Description\tOperation\tHours
    /// </summary>
    private string FormatOperationsForClipboard(List<OperationLine> lines)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            // Tab-separated: Description, Operation Type, Hours
            if (line.Hours > 0)
            {
                sb.AppendLine($"{line.Description}\t{line.Operation}\t{line.Hours:N1}");
            }
            else
            {
                sb.AppendLine($"{line.Description}\t{line.Operation}\t");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Add bot message with Copy Operations and Send to Export buttons
    /// </summary>
    private void AddBotMessageWithOperationButtons(string message, List<string>? relatedTopics = null)
    {
        var container = new StackPanel { Spacing = 8 };

        var bubble = CreateMessageBubble(message, isUser: false);
        container.Children.Add(bubble);
        _conversationLog.Add($"ASSISTANT: {message}");

        // Operation action buttons
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Copy Operations button
        var copyButton = new Button
        {
            Content = "📋 Copy Operations",
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
        copyButton.Click += CopyOperations_Click;
        actionPanel.Children.Add(copyButton);

        // Send to Export button
        var exportButton = new Button
        {
            Content = "📤 Send to Export",
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 150, 80)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
        exportButton.Click += SendToExport_Click;
        actionPanel.Children.Add(exportButton);

        // Add All Additionals button
        var additionalsButton = new Button
        {
            Content = "➕ Add Additionals",
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 100, 80, 150)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
        additionalsButton.Click += AddAdditionals_Click;
        actionPanel.Children.Add(additionalsButton);

        container.Children.Add(actionPanel);

        // Related topics
        if (relatedTopics != null && relatedTopics.Count > 0)
        {
            var relatedPanel = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Spacing = 4
            };

            relatedPanel.Children.Add(new TextBlock
            {
                Text = "Related:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });

            var buttonsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

            foreach (var topic in relatedTopics.Take(4))
            {
                var topicButton = new Button
                {
                    Content = topic,
                    FontSize = 10,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 4, 8, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(10)
                };
                topicButton.Click += (s, e) => SendMessage(topic);
                buttonsPanel.Children.Add(topicButton);
            }

            relatedPanel.Children.Add(buttonsPanel);
            container.Children.Add(relatedPanel);
        }

        _chatHistory.Children.Add(container);
        ScrollToBottom();
    }

    private async void CopyOperations_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOperationsText))
        {
            return;
        }

        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(_lastOperationsText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        if (sender is Button btn)
        {
            var originalContent = btn.Content;
            btn.Content = "✓ Copied!";
            await System.Threading.Tasks.Task.Delay(1500);
            btn.Content = originalContent;
        }
    }

    private void SendToExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOperationsText) || _lastOperationLines.Count == 0)
        {
            AddBotMessage("No operations to export. Enter an operation first.");
            return;
        }

        // Convert operation lines to Model Operations for proper clipboard formatting
        var modelOperations = _lastOperationLines
            .Where(l => l.Hours > 0) // Only lines with hours
            .Select(l => new McstudDesktop.Models.Operation
            {
                OperationType = l.LineType == "Refinish"
                    ? McstudDesktop.Models.OperationType.Refinish
                    : (l.Operation == "R&I" ? McstudDesktop.Models.OperationType.RemoveAndInstall : McstudDesktop.Models.OperationType.Repair),
                Description = l.Description.Trim(),
                Quantity = 1,
                Price = l.Cost,
                LaborHours = l.LineType == "Refinish" ? 0 : l.Hours,
                RefinishHours = l.LineType == "Refinish" ? l.Hours : 0,
                Category = l.LineType
            })
            .ToList();

        if (modelOperations.Count == 0)
        {
            AddBotMessage("No valid operation lines to export.");
            return;
        }

        // Load operations into VirtualClipboardService for display
        VirtualClipboardService.Instance.SetOperations(modelOperations, "Chat - Generated Estimate");

        // ALSO copy to Windows clipboard in CCC Desktop format so Export tab can use it
        ClipboardExportService.CopyToClipboard(modelOperations, "CCC Desktop");

        AddBotMessage($"📤 {modelOperations.Count} operations sent to Export tab. Click Type It or Clip It to export.");

        // Navigate to Export tab
        OnNavigateToExport?.Invoke(this, EventArgs.Empty);
    }

    private void AddAdditionals_Click(object sender, RoutedEventArgs e)
    {
        // Get the last mentioned operation's additionals and add them to the operation lines
        if (_mentionedOperations.Any())
        {
            var lastOp = _mentionedOperations.Last();
            var additionals = GetAssociatedAdditionals(lastOp.PartName, lastOp.OperationType, new List<ParsedOperation>());

            var addMsg = new System.Text.StringBuilder();
            addMsg.AppendLine("➕ ADDITIONAL OPERATIONS TO CONSIDER:");
            addMsg.AppendLine("─────────────────────────────────────");

            foreach (var add in additionals.Take(15))
            {
                var timeStr = add.Hours > 0 ? $"{add.Hours:N1} hr" : "";
                var costStr = add.Cost > 0 ? $"~${add.Cost:N0}" : "";
                var details = string.Join(" ", new[] { timeStr, costStr }.Where(s => !string.IsNullOrEmpty(s)));
                addMsg.AppendLine($"• {add.Item} {details}");
            }

            addMsg.AppendLine("\nTip: Add these to your estimate as separate line items.");
            AddBotMessage(addMsg.ToString());
        }
    }

    private void ExcelEstimateBuilder_OnOperationsGenerated(object? sender, List<Services.GeneratedOperation> operations)
    {
        try
        {
            // Switch to chat tab to show the results
            SelectTab(0);

            // Clear previous operation lines and prepare for new ones
            _lastOperationLines.Clear();
            _mentionedOperations.Clear();

            // Build a comprehensive message from the generated operations
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 ESTIMATE OPERATIONS GENERATED");
            sb.AppendLine("════════════════════════════════════");
            sb.AppendLine($"Operations: {operations.Count}");
            sb.AppendLine();

            // Group operations by category
            var byCategory = operations.GroupBy(o => o.Category);
            foreach (var group in byCategory)
            {
                sb.AppendLine($"▶ {group.Key.ToUpper()}");
                sb.AppendLine("─────────────────────────────");
                foreach (var op in group)
                {
                    var valueStr = op.LaborHours > 0 ? $" ({op.LaborHours:F1} hr)" :
                                   op.RefinishHours > 0 ? $" ({op.RefinishHours:F1} rfn)" :
                                   op.Price > 0 ? $" (${op.Price:F0})" : "";
                    sb.AppendLine($"  • {op.Description}{valueStr}");

                    // Add to mentioned operations for easy export
                    _mentionedOperations.Add(new EstimateOperation
                    {
                        PartName = op.Category,
                        OperationType = op.OperationType,
                        Description = op.Description,
                        LaborHours = op.LaborHours
                    });

                    // IMPORTANT: Also add to _lastOperationLines so export works
                    _lastOperationLines.Add(new OperationLine
                    {
                        Description = op.Description,
                        Hours = op.LaborHours,
                        Cost = op.Price,
                        LineType = op.Category,
                        Operation = op.OperationType
                    });
                }
                sb.AppendLine();
            }

            // Store as text for copy operations
            var opsText = new System.Text.StringBuilder();
            foreach (var op in operations)
            {
                var value = op.LaborHours > 0 ? $"{op.LaborHours:F1} hr" :
                            op.RefinishHours > 0 ? $"{op.RefinishHours:F1} rfn" :
                            op.Price > 0 ? $"${op.Price:F0}" : "";
                opsText.AppendLine($"{op.Description} - {value}");
            }
            _lastOperationsText = opsText.ToString();

            // Add totals
            var totalLabor = operations.Sum(o => (double)o.LaborHours);
            var totalRefinish = operations.Sum(o => (double)o.RefinishHours);
            var totalPrice = operations.Sum(o => (double)o.Price);

            sb.AppendLine("📊 TOTALS:");
            if (totalLabor > 0) sb.AppendLine($"  Labor: {totalLabor:F1} hours");
            if (totalRefinish > 0) sb.AppendLine($"  Refinish: {totalRefinish:F1} hours");
            if (totalPrice > 0) sb.AppendLine($"  Price: ${totalPrice:F0}");
            sb.AppendLine();
            sb.AppendLine("💡 Click 'Send to Export' to transfer to CCC Desktop.");

            AddBotMessage(sb.ToString());

            // Add action buttons for the generated operations
            AddOperationActionButtons();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelBuilder] Error: {ex.Message}");
            AddBotMessage($"Error generating operations: {ex.Message}");
        }
    }

    private void GhostEstimatePanel_OnOperationAccepted(object? sender, GhostOperation e)
    {
        // Add accepted ghost operation to mentioned operations for export
        _mentionedOperations.Add(new EstimateOperation
        {
            PartName = e.PartName,
            OperationType = e.OperationType,
            Description = e.Description,
            LaborHours = e.LaborHours
        });

        // Show feedback in chat
        SelectTab(0);
        AddBotMessage($"Added from Ghost Compare: {e.Description}");
    }

    private void ScreenMonitorPanel_OnFeedToChat(object? sender, McstudDesktop.Models.ScreenOcrResult ocrResult)
    {
        // Switch to Chat tab and prefill the input with OCR text
        SelectTab(0);
        _inputBox.Text = ScreenMonitorPanel.FormatResultForChat(ocrResult);
        _inputBox.Focus(FocusState.Programmatic);

        // Persist OCR data to estimate history
        if (ocrResult.DetectedOperations.Count > 0)
        {
            McStudDesktop.Services.EstimatePersistenceHelper.PersistAndMine(
                McStudDesktop.Services.EstimatePersistenceHelper.ConvertFromOcr(ocrResult));
        }
    }

    private void ScreenMonitorPanel_OnLoadToBuilder(object? sender, McstudDesktop.Models.ScreenOcrResult ocrResult)
    {
        var operations = ocrResult.DetectedOperations;
        // Load OCR-detected operations into the estimate builder and switch to that tab
        _estimateBuilderPanel?.AddOperationsFromOcr(operations);
        SelectTab(1);
        AddBotMessage($"Loaded {operations.Count} operations from Screen OCR into the estimate builder.");

        // Persist OCR data to estimate history
        if (operations.Count > 0)
        {
            McStudDesktop.Services.EstimatePersistenceHelper.PersistAndMine(
                McStudDesktop.Services.EstimatePersistenceHelper.ConvertFromOcr(ocrResult));
        }
    }

    private void AddOperationActionButtons()
    {
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(48, 8, 16, 8)
        };

        var copyBtn = new Button
        {
            Content = "📋 Copy Operations",
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4)
        };
        copyBtn.Click += CopyOperations_Click;

        var exportBtn = new Button
        {
            Content = "📤 Send to Export",
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 140, 70)),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4)
        };
        exportBtn.Click += SendToExport_Click;

        buttonPanel.Children.Add(copyBtn);
        buttonPanel.Children.Add(exportBtn);
        _chatHistory.Children.Add(buttonPanel);

        // Scroll to bottom
        _scrollViewer.UpdateLayout();
        _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
    }

    /// <summary>
    /// Get P-Page included/not included info for operation
    /// </summary>
    private (List<string> included, List<string> notIncluded) GetPPageInfo(string part, string opType, List<ParsedOperation> parsedOps)
    {
        var included = new List<string>();
        var notIncluded = new List<string>();
        var partLower = part.ToLower();
        var opLower = opType.ToLower();

        // Standard INCLUDED items
        if (opLower.Contains("replace"))
        {
            included.Add("Remove old part");
            included.Add("Install new part");
            included.Add("Transfer hardware (bolted components)");
            included.Add("Seam sealer AT attachment points only");
        }
        if (opLower.Contains("repair"))
        {
            included.Add("Straighten/repair damaged area");
            included.Add("Prepare for refinish");
        }

        // Standard NOT INCLUDED items from data
        if (_notIncludedData != null)
        {
            // Refinish operations
            if (parsedOps.Any(o => o.OperationType is "Refinish" or "Blend" or "Clear Coat"))
            {
                notIncluded.Add("Color tinting/matching");
                notIncluded.Add("Flex additive (on plastic)");
                notIncluded.Add("Adhesion promoter (on plastic)");
                notIncluded.Add("Denib/final sand");
                notIncluded.Add("Color sand and buff");

                if (partLower.Contains("bumper"))
                {
                    notIncluded.Add("Backside masking (radar zone)");
                }
            }

            // Structural/welded panels
            if (partLower.Contains("quarter") || partLower.Contains("roof") ||
                partLower.Contains("rocker") || partLower.Contains("pillar"))
            {
                notIncluded.Add("Weld-through primer");
                notIncluded.Add("Corrosion protection (cavities)");
                notIncluded.Add("Seam sealer beyond attachment points");
                notIncluded.Add("Sound deadener application");
                notIncluded.Add("Test welds");
                notIncluded.Add("Rivet debris removal/access");
            }

            // Always NOT INCLUDED
            notIncluded.Add("Pre-repair scan");
            notIncluded.Add("Post-repair scan");

            // Glass
            if (partLower.Contains("windshield") || partLower.Contains("glass"))
            {
                notIncluded.Add("ADAS camera calibration");
                notIncluded.Add("Urethane/adhesive materials");
                notIncluded.Add("Molding kits");
            }
        }

        return (included, notIncluded);
    }

    /// <summary>
    /// Get additional operations commonly associated with this repair
    /// </summary>
    private List<(string Item, decimal Hours, decimal Cost)> GetAssociatedAdditionals(string part, string opType, List<ParsedOperation> parsedOps)
    {
        var additionals = new List<(string Item, decimal Hours, decimal Cost)>();
        var partLower = part.ToLower();
        var opLower = opType.ToLower();

        // Bumper operations
        if (partLower.Contains("bumper"))
        {
            additionals.Add(("Flex Additive", 0, 15));
            additionals.Add(("Adhesion Promoter", 0.2m, 12));
            if (opLower.Contains("replace"))
            {
                additionals.Add(("Transfer Components (sensors, brackets)", 0.5m, 0));
            }
            additionals.Add(("R&I Grille", 0.3m, 0));
            additionals.Add(("R&I Fog Lights", 0.3m, 0));
            additionals.Add(("R&I Parking Sensors", 0.2m, 0));
            additionals.Add(("Front Radar Calibration", 0.8m, 350));
        }

        // Quarter panel operations
        if (partLower.Contains("quarter"))
        {
            additionals.Add(("Weld-Through Primer", 0.3m, 18));
            additionals.Add(("Corrosion Protection (cavities)", 0.5m, 45));
            additionals.Add(("Seam Sealer", 0.8m, 35));
            additionals.Add(("Sound Deadener", 0.3m, 25));
            additionals.Add(("R&I Tail Light", 0.3m, 0));
            additionals.Add(("R&I Fuel Door", 0.2m, 0));
            additionals.Add(("R&I Interior Trim", 0.5m, 0));
            additionals.Add(("Pre/Post Measurements", 0.5m, 0));
            additionals.Add(("Adjacent Panel Blend", 1.0m, 0));
            additionals.Add(("Blind Spot Sensor Calibration", 0.5m, 250));
        }

        // Windshield
        if (partLower.Contains("windshield"))
        {
            additionals.Add(("ADAS Camera Calibration", 1.0m, 350));
            additionals.Add(("R&I Rain Sensor", 0.2m, 0));
            additionals.Add(("R&I Rearview Mirror", 0.2m, 0));
            additionals.Add(("R&I Cowl Cover", 0.3m, 0));
            additionals.Add(("Urethane/Adhesive", 0, 35));
        }

        // Door operations
        if (partLower.Contains("door") && !partLower.Contains("fuel"))
        {
            additionals.Add(("R&I Door Trim Panel", 0.3m, 0));
            additionals.Add(("R&I Mirror", 0.3m, 0));
            additionals.Add(("R&I Window Regulator", 0.5m, 0));
            additionals.Add(("Transfer Hardware", 0.5m, 0));
            additionals.Add(("Corrosion Protection (hem)", 0.2m, 15));
            additionals.Add(("Adjacent Panel Blend", 1.0m, 0));
        }

        // Hood operations
        if (partLower.Contains("hood"))
        {
            additionals.Add(("Transfer Hood Insulator", 0.3m, 0));
            additionals.Add(("Blend Fenders (both)", 1.0m, 0));
            additionals.Add(("Adjust Hood Hinges", 0.3m, 0));
        }

        // Fender operations
        if (partLower.Contains("fender"))
        {
            additionals.Add(("R&I Fender Liner", 0.3m, 0));
            additionals.Add(("Blend Hood", 0.5m, 0));
            additionals.Add(("Blend Door", 0.5m, 0));
            additionals.Add(("Transfer Moldings", 0.2m, 0));
        }

        // Roof operations
        if (partLower.Contains("roof"))
        {
            additionals.Add(("R&I Headliner", 2.0m, 0));
            additionals.Add(("Weld-Through Primer", 0.4m, 25));
            additionals.Add(("Corrosion Protection (pillars)", 0.8m, 65));
            additionals.Add(("Seam Sealer", 1.0m, 45));
            additionals.Add(("R&I Sunroof (if equipped)", 1.5m, 0));
        }

        // Any refinish - add common items
        if (parsedOps.Any(o => o.OperationType is "Refinish" or "Blend"))
        {
            additionals.Add(("Clear Coat", 0.4m, 0));
            additionals.Add(("Denib", 0.2m, 0));
            additionals.Add(("Color Sand/Buff", 0.4m, 0));
            additionals.Add(("Color Tint/Match", 0, 45));
        }

        // Always consider scans
        additionals.Add(("Pre-Repair Scan", 0.5m, 0));
        additionals.Add(("Post-Repair Scan", 0.5m, 0));

        // Remove duplicates
        return additionals.GroupBy(a => a.Item).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// Get relevant DEG inquiries for this operation
    /// </summary>
    private List<DEGInquiry> GetRelevantDEGInquiries(string part, string opType)
    {
        if (_degInquiries == null) return new List<DEGInquiry>();

        var results = new List<DEGInquiry>();
        var partLower = part.ToLower();
        var opLower = opType.ToLower();

        foreach (var deg in _degInquiries)
        {
            var titleLower = deg.Title.ToLower();
            var categoryLower = deg.Category.ToLower();

            // Match by category/part
            if (partLower.Contains("bumper") && (titleLower.Contains("bumper") || titleLower.Contains("radar")))
            {
                results.Add(deg);
            }
            else if (partLower.Contains("quarter") && (titleLower.Contains("quarter") || titleLower.Contains("seam") || titleLower.Contains("corrosion")))
            {
                results.Add(deg);
            }
            else if ((partLower.Contains("roof") || partLower.Contains("quarter") || partLower.Contains("rocker"))
                     && (titleLower.Contains("seam") || titleLower.Contains("rivet")))
            {
                results.Add(deg);
            }
            else if (categoryLower.Contains("scanning") || titleLower.Contains("scan"))
            {
                results.Add(deg);
            }
            else if (opLower.Contains("refinish") && categoryLower.Contains("refinish"))
            {
                results.Add(deg);
            }
        }

        // Always include the seam sealer and scan DEGs as they apply broadly
        var seam = _degInquiries.FirstOrDefault(d => d.InquiryNumber.Contains("23010"));
        var scan = _degInquiries.FirstOrDefault(d => d.InquiryNumber.Contains("12928"));
        if (seam != null && !results.Contains(seam)) results.Add(seam);
        if (scan != null && !results.Contains(scan)) results.Add(scan);

        return results.Distinct().Take(4).ToList();
    }

    private ProcessedMessage ProcessMessage(string message)
    {
        var result = new ProcessedMessage { OriginalMessage = message };
        var msgLower = message.ToLowerInvariant();

        // Check for Operation Description Builder requests FIRST
        // "fix dent" → "PDR repair, 2" crease, door panel, conventional access"
        if (_descriptionBuilder.IsDescriptionRequest(message))
        {
            result.IsDescriptionBuilderRequest = true;
            return result;
        }

        // Check for AI Advisor queries FIRST
        // "what am i missing", "similar estimates", "what does State Farm pay", "review my estimate", etc.
        if (_advisorService.IsAdvisorQuery(message))
        {
            result.IsAdvisorQuery = true;
            result.DetectedPart = ExtractPart(msgLower);
            result.DetectedOperation = ExtractOperation(msgLower);
            return result;
        }

        // Check for suggestion requests - "suggest bumper cover replace" or "what do I need for bumper replace"
        var suggestionTriggers = new[] { "suggest", "help me with", "what manual lines" };
        if (suggestionTriggers.Any(t => msgLower.StartsWith(t) || msgLower.Contains(t)))
        {
            var part = ExtractPart(msgLower);
            var op = ExtractOperation(msgLower);
            if (!string.IsNullOrEmpty(part))
            {
                result.IsSuggestionRequest = true;
                result.DetectedPart = part;
                result.DetectedOperation = string.IsNullOrEmpty(op) ? "Replace" : op;
                return result;
            }
        }

        // Check for supplement-related queries
        var supplementKeywords = new[] { "check", "supplement", "missing", "forgot", "need for", "what about" };
        var partKeywords = new[] { "bumper", "quarter", "fender", "door", "hood", "roof", "windshield", "glass", "panel" };
        var operationKeywords = new[] { "replace", "replacing", "repair", "repairing", "refinish", "paint", "section", "r&i" };

        bool hasPart = partKeywords.Any(k => msgLower.Contains(k));
        bool hasOperation = operationKeywords.Any(k => msgLower.Contains(k));

        // FIRST: Check for operation entry with times (ARMED UP mode)
        // Pattern: "replacing bumper cover, 1.0 trial fit, 3.0 refinish"
        var parsedOps = ParseOperationTimes(message);
        if (hasPart && (hasOperation || parsedOps.Any()))
        {
            result.IsFullOperationEntry = true;
            result.DetectedPart = ExtractPart(msgLower);
            result.DetectedOperation = ExtractOperation(msgLower);
            result.ParsedOperations = parsedOps;
            return result;
        }

        // ADAS check
        if (msgLower.Contains("adas") || msgLower.Contains("calibrat") || msgLower.Contains("sensor") ||
            (msgLower.Contains("windshield") && !msgLower.Contains("why")))
        {
            result.IsADASCheck = true;
            result.DetectedPart = ExtractPart(msgLower);
            result.DetectedOperation = ExtractOperation(msgLower);
            return result;
        }

        // Supplement check (legacy mode)
        bool hasSupplementIntent = supplementKeywords.Any(k => msgLower.Contains(k));
        if (hasSupplementIntent && hasPart)
        {
            result.IsSupplementCheck = true;
            result.DetectedPart = ExtractPart(msgLower);
            result.DetectedOperation = ExtractOperation(msgLower);
            return result;
        }

        // Training/Why questions
        var trainingKeywords = new[] { "why", "what is", "explain", "not included", "deg", "how do" };
        if (trainingKeywords.Any(k => msgLower.Contains(k)))
        {
            result.IsTrainingQuestion = true;
            result.TrainingTopic = ExtractTrainingTopic(msgLower);
            return result;
        }

        // 3-stage paint
        if (msgLower.Contains("3-stage") || msgLower.Contains("tri-coat") || msgLower.Contains("three stage") || msgLower.Contains("pearl"))
        {
            result.IsTrainingQuestion = true;
            result.TrainingTopic = "3-stage paint";
            return result;
        }

        // Blend calculation
        if (msgLower.Contains("blend") && (msgLower.Contains("what") || msgLower.Contains("which") || msgLower.Contains("need")))
        {
            result.IsSupplementCheck = true;
            result.IsBlendQuestion = true;
            result.DetectedPart = ExtractPart(msgLower);
            return result;
        }

        return result;
    }

    /// <summary>
    /// Parse operation times from natural language input
    /// Examples: "1.0 trial fit", "3.0 refinish", "2.5 body", "0.8 r&i"
    /// </summary>
    private List<ParsedOperation> ParseOperationTimes(string message)
    {
        var operations = new List<ParsedOperation>();
        var msgLower = message.ToLowerInvariant();

        // Patterns to match: "1.0 trial fit", "3.0 refinish", "2.5 body labor", etc.
        var patterns = new Dictionary<string, string>
        {
            [@"(\d+\.?\d*)\s*(trial\s*fit|test\s*fit)"] = "Trial Fit",
            [@"(\d+\.?\d*)\s*(refinish|rfn|paint)"] = "Refinish",
            [@"(\d+\.?\d*)\s*(body|body\s*labor|bdy)"] = "Body Labor",
            [@"(\d+\.?\d*)\s*(r&i|r\+i|remove.*install|r/i)"] = "R&I",
            [@"(\d+\.?\d*)\s*(blend)"] = "Blend",
            [@"(\d+\.?\d*)\s*(clear|clearcoat|clr)"] = "Clear Coat",
            [@"(\d+\.?\d*)\s*(mech|mechanical)"] = "Mechanical",
            [@"(\d+\.?\d*)\s*(frame|struct|structural)"] = "Frame/Structural",
            [@"(\d+\.?\d*)\s*(repair|rpr)(?!\s*scan)"] = "Repair",
            [@"(\d+\.?\d*)\s*(overhaul|o/h|oh)"] = "Overhaul",
        };

        foreach (var kvp in patterns)
        {
            var regex = new Regex(kvp.Key, RegexOptions.IgnoreCase);
            var match = regex.Match(msgLower);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var hours))
            {
                operations.Add(new ParsedOperation
                {
                    OperationType = kvp.Value,
                    Hours = hours
                });
            }
        }

        return operations;
    }

    private string ExtractPart(string msg)
    {
        var parts = new Dictionary<string, string>
        {
            ["front bumper"] = "Front Bumper Cover",
            ["rear bumper"] = "Rear Bumper Cover",
            ["bumper cover"] = "Bumper Cover",
            ["bumper"] = "Bumper Cover",
            ["quarter panel"] = "Quarter Panel",
            ["quarter"] = "Quarter Panel",
            ["left fender"] = "Left Fender",
            ["right fender"] = "Right Fender",
            ["fender"] = "Fender",
            ["hood"] = "Hood",
            ["roof"] = "Roof",
            ["trunk"] = "Trunk Lid",
            ["door"] = "Door",
            ["windshield"] = "Windshield",
            ["back glass"] = "Back Glass",
            ["rocker"] = "Rocker Panel"
        };

        foreach (var kvp in parts)
        {
            if (msg.Contains(kvp.Key))
                return kvp.Value;
        }

        return "Panel";
    }

    private string ExtractOperation(string msg)
    {
        if (msg.Contains("replace") || msg.Contains("rpl")) return "Replace";
        if (msg.Contains("repair") || msg.Contains("rpr")) return "Repair";
        if (msg.Contains("refinish") || msg.Contains("paint") || msg.Contains("rfn")) return "Refinish";
        if (msg.Contains("section")) return "Section";
        if (msg.Contains("r&i") || msg.Contains("r+i")) return "R&I";
        return "Replace"; // Default
    }

    private string ExtractTrainingTopic(string msg)
    {
        if (msg.Contains("flex")) return "flex additive";
        if (msg.Contains("adhesion")) return "adhesion promoter";
        if (msg.Contains("corrosion")) return "corrosion protection";
        if (msg.Contains("weld") && msg.Contains("primer")) return "weld-thru primer";
        if (msg.Contains("seam")) return "seam sealer";
        if (msg.Contains("scan")) return "diagnostic scan";
        if (msg.Contains("blend")) return "blend";
        if (msg.Contains("not included")) return "not included";
        if (msg.Contains("deg")) return "DEG";
        if (msg.Contains("adas") || msg.Contains("calibrat")) return "ADAS calibration";
        return "general";
    }

    private void AddSupplementResponse(ProcessedMessage processed)
    {
        var part = processed.DetectedPart;
        var opType = processed.DetectedOperation;

        // Handle blend question
        if (processed.IsBlendQuestion)
        {
            var blendResult = _helperService.CalculateBlendPanels(new List<string> { part });
            var blendMsg = $"🎨 BLEND CALCULATOR for {part}:\n\n";

            if (blendResult.BlendPanels.Any())
            {
                blendMsg += "Adjacent panels that may need blend:\n";
                foreach (var panel in blendResult.BlendPanels)
                {
                    blendMsg += $"  • {panel}\n";
                }
                blendMsg += $"\nEstimated blend time: {blendResult.EstimatedBlendHours:N1} hours";
            }
            else
            {
                blendMsg += "No adjacent panels found in database.";
            }

            AddBotMessage(blendMsg, new List<string> { $"Check {part} supplements", "3-stage paint", "What's NOT INCLUDED?" });
            return;
        }

        // Build operation for analysis
        var operation = new EstimateOperation
        {
            OperationType = opType,
            PartName = part,
            Description = $"{opType} {part}"
        };

        _mentionedOperations.Add(operation);

        // Run supplement analysis
        var input = new EstimateAnalysisInput
        {
            Operations = new List<EstimateOperation> { operation },
            EstimateTotal = 3000 // Assume over threshold
        };

        var result = _supplementService.AnalyzeEstimate(input);

        // Build response
        var response = $"📋 SUPPLEMENT CHECK: {opType} {part}\n\n";

        if (result.Suggestions.Any())
        {
            // Critical & High Priority
            var important = result.CriticalItems.Concat(result.HighPriorityItems).ToList();
            if (important.Any())
            {
                response += "🔴 HIGH PRIORITY:\n";
                foreach (var item in important)
                {
                    response += $"  • {item.Item}";
                    if (item.EstimatedValue > 0) response += $" (~${item.EstimatedValue:N0})";
                    response += "\n";
                }
                response += "\n";
            }

            // Medium Priority
            if (result.MediumPriorityItems.Any())
            {
                response += "🟡 ALSO CONSIDER:\n";
                foreach (var item in result.MediumPriorityItems)
                {
                    response += $"  • {item.Item}\n";
                }
                response += "\n";
            }

            // Low Priority
            if (result.LowPriorityItems.Any())
            {
                response += $"🟢 {result.LowPriorityItems.Count} low priority items (denib, etc.)\n\n";
            }

            response += $"💰 Total potential supplements: ${result.TotalSuggestedValue:N0}";
        }
        else
        {
            response += "No commonly missed items detected for this operation.";
        }

        // Generate related topics
        var related = new List<string>();
        if (part.Contains("Bumper")) related.Add("ADAS for bumper");
        if (part.Contains("Quarter") || part.Contains("Roof")) related.Add("Why corrosion protection?");
        if (opType == "Refinish") related.Add("3-stage paint");
        related.Add("What's NOT INCLUDED?");

        AddBotMessage(response, related);
    }

    private void AddADASResponse(ProcessedMessage processed)
    {
        var part = processed.DetectedPart;
        var opType = processed.DetectedOperation;

        var operation = new EstimateOperation
        {
            OperationType = opType,
            PartName = part,
            Description = $"{opType} {part}"
        };

        var result = _adasService.AnalyzeForADAS(new List<EstimateOperation> { operation });

        var response = $"🎯 ADAS CHECK: {opType} {part}\n\n";

        if (result.RequiredCalibrations.Any())
        {
            response += "CALIBRATIONS REQUIRED:\n";
            foreach (var cal in result.RequiredCalibrations)
            {
                response += $"\n📡 {cal.SensorName}\n";
                response += $"   Type: {cal.CalibrationType}\n";
                response += $"   {cal.Explanation}\n";
                if (cal.EstimatedCost > 0)
                    response += $"   Est: ${cal.EstimatedCost:N0} ({cal.EstimatedTime:N1} hr)\n";
                if (!string.IsNullOrEmpty(cal.OEMNotes))
                    response += $"   ⚠️ {cal.OEMNotes}\n";
            }

            response += $"\n💰 Total ADAS: ${result.TotalCalibrationCost:N0}";
        }
        else
        {
            response += "No ADAS calibrations detected for this operation.\n\n";
            response += "Note: ADAS requirements depend on vehicle equipment. Check if vehicle has:\n";
            response += "  • Forward camera (windshield)\n";
            response += "  • Front radar (bumper/grille)\n";
            response += "  • Blind spot sensors (rear quarters/bumper)";
        }

        // Painting considerations
        if (result.PaintingConsiderations.Any())
        {
            response += "\n\n⚠️ PAINT WARNINGS:\n";
            foreach (var warning in result.PaintingConsiderations)
            {
                response += $"  {warning}\n";
            }
        }

        var related = new List<string> { $"Check {part} supplements", "What's NOT INCLUDED?" };
        AddBotMessage(response, related);
    }

    /// <summary>
    /// Smart suggestions response - shows learned patterns for a part
    /// Based ONLY on uploaded estimates - no assumptions
    /// Each line has a button to reveal learned status (on-demand)
    /// </summary>
    private void AddSuggestionResponse(ProcessedMessage processed)
    {
        var part = processed.DetectedPart;
        var operation = processed.DetectedOperation;

        // Get suggestions from the smart suggestion service (learned data only)
        var suggestionService = SmartSuggestionService.Instance;
        var suggestions = suggestionService.GetSuggestionsForPart(part, operation);

        // Main container for the suggestion response
        var container = new StackPanel { Spacing = 8 };

        // Header bubble
        var headerText = $"💡 SUGGESTIONS: {operation} {part}";
        if (!string.IsNullOrEmpty(suggestions.MatchDescription))
        {
            headerText += $"\n📊 {suggestions.MatchDescription}";
        }

        var headerBubble = CreateMessageBubble(headerText, isUser: false);
        container.Children.Add(headerBubble);
        _conversationLog.Add($"ASSISTANT: {headerText}");

        // Manual operations from learned data
        if (suggestions.ManualOperations.Count > 0)
        {
            // Summary totals at the top
            if (suggestions.TotalLaborHours > 0 || suggestions.TotalRefinishHours > 0)
            {
                var totalsText = $"Total: {suggestions.TotalLaborHours:N1} body hrs, {suggestions.TotalRefinishHours:N1} refn hrs";
                if (suggestions.EstimatedValue > 0)
                    totalsText += $" | Est. Value: ${suggestions.EstimatedValue:N0}";

                var totalsBlock = new TextBlock
                {
                    Text = totalsText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    Margin = new Thickness(8, 4, 8, 8)
                };
                container.Children.Add(totalsBlock);
            }

            // Operations panel with expandable items
            var opsPanel = new StackPanel
            {
                Spacing = 4,
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(8)
            };

            // Header for operations section
            var opsHeader = new TextBlock
            {
                Text = "📝 LEARNED MANUAL OPERATIONS (click ℹ️ for details):",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };
            opsPanel.Children.Add(opsHeader);

            foreach (var op in suggestions.ManualOperations.Take(12))
            {
                var opRow = CreateSuggestionOpRow(op);
                opsPanel.Children.Add(opRow);
            }

            container.Children.Add(opsPanel);
        }
        else
        {
            // No data message
            var noDataPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 45, 40)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var noDataStack = new StackPanel { Spacing = 4 };
            noDataStack.Children.Add(new TextBlock
            {
                Text = "No learned data for this part/operation yet.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 150)),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            noDataStack.Children.Add(new TextBlock
            {
                Text = "To learn patterns:\n• Upload completed estimates with this part\n• The system will learn what manual lines (#) go with it\n• Future suggestions will show what you actually used",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });

            noDataPanel.Child = noDataStack;
            container.Children.Add(noDataPanel);
        }

        // Related topics buttons
        var related = new List<string>
        {
            $"suggest {part} repair",
            $"suggest {part} R&I",
            $"replacing {part}, 1.0 trial fit, 3.0 refinish"
        };

        var relatedPanel = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Spacing = 4
        };

        relatedPanel.Children.Add(new TextBlock
        {
            Text = "Try asking:",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
        });

        var buttonsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var topic in related.Take(4))
        {
            var topicButton = new Button
            {
                Content = topic,
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 4, 8, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10)
            };
            topicButton.Click += (s, e) => SendMessage(topic);
            buttonsPanel.Children.Add(topicButton);
        }

        relatedPanel.Children.Add(buttonsPanel);
        container.Children.Add(relatedPanel);

        // Track message
        _messages.Add(new ChatMessage
        {
            Content = headerText,
            IsUser = false,
            Timestamp = DateTime.Now
        });

        _chatHistory.Children.Add(container);
        SaveChatHistory();
        UpdateQuickReplies();
        ScrollToBottom();
    }

    /// <summary>
    /// Creates a suggestion operation row with an info button to show learned status
    /// </summary>
    private Border CreateSuggestionOpRow(SmartSuggestionOp op)
    {
        var rowBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var rowStack = new StackPanel { Spacing = 4 };

        // Main row with operation and info button
        var mainRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Operation description
        var hours = "";
        if (op.LaborHours > 0) hours += $" {op.LaborHours:N1}h";
        if (op.RefinishHours > 0) hours += $" {op.RefinishHours:N1}r";
        if (op.Price > 0) hours += $" ${op.Price:N0}";

        var descText = new TextBlock
        {
            Text = $"• {op.Description}{hours}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(descText, 0);
        mainRow.Children.Add(descText);

        // Learned indicator (small badge showing times used)
        if (op.TimesUsed > 0)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 80, 80, 100)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = $"{op.TimesUsed}x",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 200))
            };
            Grid.SetColumn(badge, 1);
            mainRow.Children.Add(badge);
        }

        // Info button to reveal learned details
        var infoButton = new Button
        {
            Content = "ℹ️",
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 32,
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 80)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(infoButton, "Show learned status");
        Grid.SetColumn(infoButton, 2);
        mainRow.Children.Add(infoButton);

        rowStack.Children.Add(mainRow);

        // Details panel (hidden by default)
        var detailsPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 45, 50, 60)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(16, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var detailsStack = new StackPanel { Spacing = 2 };

        // Build details content
        if (!string.IsNullOrEmpty(op.Reason))
        {
            detailsStack.Children.Add(new TextBlock
            {
                Text = $"📊 {op.Reason}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 200, 150)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (op.Confidence > 0)
        {
            detailsStack.Children.Add(new TextBlock
            {
                Text = $"Confidence: {op.Confidence:P0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 180))
            });
        }

        detailsStack.Children.Add(new TextBlock
        {
            Text = $"Source: {op.Source}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 150))
        });

        detailsPanel.Child = detailsStack;
        rowStack.Children.Add(detailsPanel);

        // Toggle visibility on info button click
        infoButton.Click += (s, e) =>
        {
            if (detailsPanel.Visibility == Visibility.Collapsed)
            {
                detailsPanel.Visibility = Visibility.Visible;
                infoButton.Content = "▼";
                ToolTipService.SetToolTip(infoButton, "Hide learned status");
            }
            else
            {
                detailsPanel.Visibility = Visibility.Collapsed;
                infoButton.Content = "ℹ️";
                ToolTipService.SetToolTip(infoButton, "Show learned status");
            }
        };

        rowBorder.Child = rowStack;
        return rowBorder;
    }

    private void AddTrainingResponse(ProcessedMessage processed)
    {
        var topic = processed.TrainingTopic;
        var response = "";

        switch (topic.ToLower())
        {
            case "flex additive":
                response = "💡 FLEX ADDITIVE\n\n" +
                          "WHY: Plastic bumpers flex on impact. Standard paint is rigid and will crack if the plastic flexes.\n\n" +
                          "WHEN: Required on ALL plastic refinish - bumper covers, mirror caps, cladding, spoilers.\n\n" +
                          "NOT INCLUDED: Material cost and application time are NOT INCLUDED in refinish labor times.\n\n" +
                          "TIP: This is one of the most commonly missed supplement items!";
                break;

            case "adhesion promoter":
                response = "💡 ADHESION PROMOTER\n\n" +
                          "WHY: Paint doesn't properly bond to bare plastic. Adhesion promoter creates a chemical bond.\n\n" +
                          "WHEN: Required when painting raw/bare plastic surfaces (new bumpers, bare repair areas).\n\n" +
                          "NOT INCLUDED: Application time (typically 0.2 hr) and material NOT INCLUDED.";
                break;

            case "corrosion protection":
                response = "💡 CORROSION PROTECTION\n\n" +
                          "WHY: Welding burns off factory corrosion protection. Without replacement, the repair will rust from the inside out.\n\n" +
                          "WHEN: Required on ALL welded panel repairs - quarter panels, roof, rockers, floor, rails.\n\n" +
                          "DEG: Inquiry #23010 confirms corrosion protection is NOT INCLUDED.\n\n" +
                          "INCLUDES:\n" +
                          "  • Cavity wax in enclosed sections\n" +
                          "  • Seam sealer on visible joints\n" +
                          "  • Undercoating on exposed areas";
                break;

            case "weld-thru primer":
                response = "💡 WELD-THRU PRIMER\n\n" +
                          "WHY: Provides corrosion protection at weld joints where bare metal meets bare metal.\n\n" +
                          "WHEN: Applied BEFORE welding on mating surfaces.\n\n" +
                          "OEM REQUIREMENT: Most manufacturers require weld-thru primer per position statements.\n\n" +
                          "NOT INCLUDED: Material and application time NOT INCLUDED in panel replacement times.";
                break;

            case "seam sealer":
                response = "💡 SEAM SEALER\n\n" +
                          "WHY: Prevents water intrusion and replicates factory appearance.\n\n" +
                          "DEG #23010: Seam sealer AT attachment points IS included. Additional seam sealer BEYOND attachment points is NOT INCLUDED.\n\n" +
                          "DEG #23332: Masking for sprayable seam sealer is NOT INCLUDED.\n\n" +
                          "TYPES:\n" +
                          "  • Brushable seam sealer\n" +
                          "  • Sprayable (texture match)\n" +
                          "  • Heavy-bodied for larger gaps";
                break;

            case "diagnostic scan":
                response = "💡 DIAGNOSTIC SCANS\n\n" +
                          "PRE-REPAIR SCAN:\n" +
                          "  • Documents existing codes before work\n" +
                          "  • Proves pre-existing conditions\n" +
                          "  • NOT INCLUDED in any labor times\n\n" +
                          "POST-REPAIR SCAN:\n" +
                          "  • Verifies no new codes set during repair\n" +
                          "  • Confirms all systems operational\n" +
                          "  • NOT INCLUDED in any labor times\n\n" +
                          "IN-PROCESS SCAN (DEG #17537):\n" +
                          "  • During repair (e.g., after battery reconnect)\n" +
                          "  • Only CCC offers as selection\n" +
                          "  • NOT INCLUDED in any system";
                break;

            case "blend":
                response = "💡 BLEND / COLOR MATCH\n\n" +
                          "WHY: New paint won't perfectly match aged original paint due to:\n" +
                          "  • Paint weathering/fading\n" +
                          "  • Slight formula variations\n" +
                          "  • Application differences\n\n" +
                          "WHEN: Refinishing any panel, adjacent panels typically need blend.\n\n" +
                          "EXAMPLE: Hood refinish → blend both fenders\n\n" +
                          "TIP: Ask me \"Blend for [panel name]\" to see adjacent panels!";
                break;

            case "3-stage paint":
                response = "💡 3-STAGE / TRI-COAT PAINT\n\n" +
                          "WHAT: Three-layer paint system:\n" +
                          "  1. Basecoat (color)\n" +
                          "  2. Mid-coat (pearl/mica)\n" +
                          "  3. Clearcoat\n\n" +
                          "LABOR: Add 50% to refinish time!\n" +
                          "  Example: 3.0 hr refinish → 4.5 hr for tri-coat\n\n" +
                          "COMMON: White Pearl, certain reds, some metallics\n\n" +
                          "TIP: Check paint code - if it mentions 'pearl', 'tri', or 'mica', it's likely 3-stage.";
                break;

            case "not included":
                response = "💡 NOT INCLUDED OPERATIONS\n\n" +
                          "These items are NOT in database labor times:\n\n" +
                          "REFINISH:\n" +
                          "  • Flex additive (plastic)\n" +
                          "  • Adhesion promoter\n" +
                          "  • Color tinting/matching\n" +
                          "  • Denib/color sand/buff\n\n" +
                          "STRUCTURAL:\n" +
                          "  • Weld-thru primer\n" +
                          "  • Corrosion protection\n" +
                          "  • Seam sealer (beyond attachment)\n" +
                          "  • Test welds\n\n" +
                          "DIAGNOSTIC:\n" +
                          "  • Pre-repair scan\n" +
                          "  • Post-repair scan\n\n" +
                          "ADAS:\n" +
                          "  • All calibrations are separate operations";
                break;

            case "deg":
                response = "💡 DEG - DATABASE ENHANCEMENT GATEWAY\n\n" +
                          "WHAT: Industry resource where shops submit questions about estimating database accuracy.\n\n" +
                          "HOW IT WORKS:\n" +
                          "  1. Shop submits inquiry about labor time or inclusion\n" +
                          "  2. DEG researches with CCC/Mitchell/Audatex\n" +
                          "  3. Response clarifies what IS and IS NOT included\n" +
                          "  4. May result in database updates\n\n" +
                          "KEY INQUIRIES:\n" +
                          "  • #23010 - Seam sealer NOT INCLUDED\n" +
                          "  • #17537 - In-process scan NOT INCLUDED\n" +
                          "  • #18914 - Rivet debris removal NOT INCLUDED\n\n" +
                          "WEBSITE: degweb.org";
                break;

            case "adas calibration":
                response = "💡 ADAS CALIBRATION\n\n" +
                          "TRIGGERS:\n" +
                          "  • Windshield R&I → Camera calibration\n" +
                          "  • Bumper work → Radar calibration\n" +
                          "  • Alignment → Steering angle + verification\n" +
                          "  • Quarter panel → Blind spot sensors\n\n" +
                          "TYPES:\n" +
                          "  • Static: In-shop with targets\n" +
                          "  • Dynamic: Road test procedure\n" +
                          "  • Both: Some systems require both\n\n" +
                          "NOT INCLUDED: ADAS calibrations are NEVER included in repair labor.";
                break;

            default:
                // Fall back to chatbot service
                var chatResponse = _chatbotService.GetResponse(processed.OriginalMessage);
                AddBotMessage(chatResponse.Message, chatResponse.RelatedTopics);
                return;
        }

        var related = new List<string> { "What's NOT INCLUDED?", "Check bumper replace", "ADAS for windshield" };
        AddBotMessage(response, related);
    }

    /// <summary>
    /// Operation Description Builder response - converts informal descriptions to professional language
    /// "fix dent" → "PDR repair, 2" crease, door panel, conventional access"
    /// </summary>
    private void AddDescriptionBuilderResponse(ProcessedMessage processed)
    {
        var result = _descriptionBuilder.BuildDescriptions(processed.OriginalMessage);

        var responseText = new System.Text.StringBuilder();
        responseText.AppendLine("📝 PROFESSIONAL DESCRIPTIONS");
        responseText.AppendLine("═══════════════════════════════════════════");

        if (!string.IsNullOrEmpty(result.DetectedOperation))
        {
            responseText.AppendLine($"🔍 Detected: {result.DetectedOperation}");
            if (!string.IsNullOrEmpty(result.DetectedPanel))
                responseText.Append($" on {result.DetectedPanel}");
            if (!string.IsNullOrEmpty(result.DetectedSize))
                responseText.Append($" ({result.DetectedSize})");
            responseText.AppendLine();
            responseText.AppendLine();
        }

        responseText.AppendLine("Choose a professional description:");
        responseText.AppendLine();

        foreach (var suggestion in result.Suggestions)
        {
            responseText.AppendLine($"  → \"{suggestion.Description}\"");
        }

        responseText.AppendLine();
        responseText.AppendLine("─────────────────────────────────────────────");
        responseText.AppendLine("💡 Tip: Click a suggestion to copy it!");

        // Create the message container with clickable suggestions
        var timestamp = DateTime.Now;
        var container = new StackPanel { Spacing = 8 };

        var bubble = CreateMessageBubbleWithTimestamp(responseText.ToString(), isUser: false, timestamp);
        container.Children.Add(bubble);
        _conversationLog.Add($"ASSISTANT: {responseText}");

        // Track message
        _messages.Add(new ChatMessage
        {
            Content = responseText.ToString(),
            IsUser = false,
            Timestamp = timestamp
        });

        // Add clickable suggestion buttons
        var suggestionsPanel = new StackPanel
        {
            Margin = new Thickness(48, 4, 48, 0),
            Spacing = 6
        };

        foreach (var suggestion in result.Suggestions)
        {
            var suggestionButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 60)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 100, 120)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var buttonContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttonContent.Children.Add(new TextBlock
            {
                Text = "📋",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            buttonContent.Children.Add(new TextBlock
            {
                Text = suggestion.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400
            });

            suggestionButton.Content = buttonContent;

            // Copy to clipboard on click
            var descToCopy = suggestion.Description;
            suggestionButton.Click += async (s, e) =>
            {
                try
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(descToCopy);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                    // Provide feedback
                    if (s is Button btn && btn.Content is StackPanel sp && sp.Children.Count > 0)
                    {
                        var icon = sp.Children[0] as TextBlock;
                        if (icon != null)
                        {
                            icon.Text = "✓";
                            await System.Threading.Tasks.Task.Delay(1000);
                            icon.Text = "📋";
                        }
                    }

                    AddBotMessage($"Copied: \"{descToCopy}\"");
                }
                catch { }
            };

            suggestionsPanel.Children.Add(suggestionButton);
        }

        container.Children.Add(suggestionsPanel);

        // Add related suggestions
        var related = new List<string>
        {
            "fix scratch door",
            "repair rust quarter",
            "describe paint bumper",
            "professional dent hood"
        };

        var relatedPanel = new StackPanel
        {
            Margin = new Thickness(48, 8, 48, 0),
            Spacing = 4
        };

        relatedPanel.Children.Add(new TextBlock
        {
            Text = "Try more:",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
        });

        var buttonsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var topic in related.Take(4))
        {
            var topicButton = new Button
            {
                Content = topic,
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 4, 8, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10)
            };
            topicButton.Click += (s, e) => SendMessage(topic);
            buttonsPanel.Children.Add(topicButton);
        }

        relatedPanel.Children.Add(buttonsPanel);
        container.Children.Add(relatedPanel);

        _chatHistory.Children.Add(container);
        SaveChatHistory();
        UpdateQuickReplies();
        ScrollToBottom();
    }

    private void AddUserMessage(string message)
    {
        var timestamp = DateTime.Now;
        var bubble = CreateMessageBubbleWithTimestamp(message, isUser: true, timestamp);
        _chatHistory.Children.Add(bubble);
        _conversationLog.Add($"YOU: {message}");

        // Track message
        _messages.Add(new ChatMessage
        {
            Content = message,
            IsUser = true,
            Timestamp = timestamp
        });

        SaveChatHistory();
        ScrollToBottom();
    }

    /// <summary>
    /// Public method for other views to post AI intelligence summaries to the chat feed.
    /// </summary>
    public void PostAISummary(string message, List<string>? followUps = null)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            AddBotMessage(message, followUps);
        });
    }

    private void AddBotMessage(string message, List<string>? relatedTopics = null)
    {
        var timestamp = DateTime.Now;
        var container = new StackPanel { Spacing = 8 };

        var bubble = CreateMessageBubbleWithTimestamp(message, isUser: false, timestamp);
        container.Children.Add(bubble);
        _conversationLog.Add($"ASSISTANT: {message}");

        // Track message
        _messages.Add(new ChatMessage
        {
            Content = message,
            IsUser = false,
            Timestamp = timestamp
        });

        if (relatedTopics != null && relatedTopics.Count > 0)
        {
            var relatedPanel = new StackPanel
            {
                Margin = new Thickness(48, 4, 48, 0),
                Spacing = 4
            };

            relatedPanel.Children.Add(new TextBlock
            {
                Text = "Try asking:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120))
            });

            var buttonsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

            foreach (var topic in relatedTopics.Take(4))
            {
                var topicButton = new Button
                {
                    Content = topic,
                    FontSize = 10,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 4, 8, 0),
                    Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 60)),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(10)
                };
                topicButton.Click += (s, e) => SendMessage(topic);
                buttonsPanel.Children.Add(topicButton);
            }

            relatedPanel.Children.Add(buttonsPanel);
            container.Children.Add(relatedPanel);
        }

        _chatHistory.Children.Add(container);
        SaveChatHistory();
        UpdateQuickReplies(); // Update quick replies based on context
        ScrollToBottom();
    }

    private StackPanel CreateMessageBubble(string message, bool isUser)
    {
        return CreateMessageBubbleWithTimestamp(message, isUser, DateTime.Now);
    }

    private StackPanel CreateMessageBubbleWithTimestamp(string message, bool isUser, DateTime timestamp)
    {
        var container = new StackPanel
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = isUser ? new Thickness(48, 0, 0, 0) : new Thickness(0, 0, 48, 0)
        };

        var bubble = new Border
        {
            Background = isUser
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 212))
                : new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = 500
        };

        var richTextBlock = new RichTextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
        paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = message });
        richTextBlock.Blocks.Add(paragraph);

        bubble.Child = richTextBlock;
        container.Children.Add(bubble);

        // Timestamp
        var timeText = new TextBlock
        {
            Text = FormatTimestamp(timestamp),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(4, 2, 4, 0)
        };
        container.Children.Add(timeText);

        if (!isUser)
        {
            var readAloudBtn = Services.TextToSpeechService.CreateSmallReadAloudButton(() => message);
            readAloudBtn.HorizontalAlignment = HorizontalAlignment.Left;
            readAloudBtn.Margin = new Thickness(4, 2, 0, 0);
            container.Children.Add(readAloudBtn);
        }

        return container;
    }

    private string FormatTimestamp(DateTime timestamp)
    {
        var now = DateTime.Now;

        if (timestamp.Date == now.Date)
        {
            // Today - just show time
            return timestamp.ToString("h:mm tt");
        }
        else if (timestamp.Date == now.Date.AddDays(-1))
        {
            // Yesterday
            return $"Yesterday {timestamp.ToString("h:mm tt")}";
        }
        else
        {
            // Older
            return timestamp.ToString("MMM d, h:mm tt");
        }
    }

    private void ScrollToBottom()
    {
        _scrollViewer.DispatcherQueue.TryEnqueue(() =>
        {
            _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
        });
    }

    private void ShowTypingIndicator()
    {
        if (_typingIndicator != null)
        {
            _typingIndicator.Visibility = Visibility.Visible;
            return;
        }

        // Create typing indicator (animated dots)
        _typingIndicator = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 55, 55, 55)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 10, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 48, 0)
        };

        var dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        // Three animated dots
        for (int i = 0; i < 3; i++)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Opacity = 0.5 + (i * 0.2) // Staggered opacity for animation effect
            };
            dotsPanel.Children.Add(dot);
        }

        _typingIndicator.Child = dotsPanel;
        _chatHistory.Children.Add(_typingIndicator);
        ScrollToBottom();
    }

    private void HideTypingIndicator()
    {
        if (_typingIndicator != null)
        {
            _chatHistory.Children.Remove(_typingIndicator);
            _typingIndicator = null;
        }
    }

    // Simple WrapPanel since WinUI doesn't have one built-in
    private class WrapPanel : Panel
    {
        public Orientation Orientation { get; set; } = Orientation.Horizontal;

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            double x = 0, y = 0, rowHeight = 0, maxWidth = 0;

            foreach (UIElement child in Children)
            {
                child.Measure(availableSize);
                var desired = child.DesiredSize;

                if (x + desired.Width > availableSize.Width && x > 0)
                {
                    y += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }

                x += desired.Width;
                maxWidth = Math.Max(maxWidth, x);
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            return new Windows.Foundation.Size(maxWidth, y + rowHeight);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;

            foreach (UIElement child in Children)
            {
                var desired = child.DesiredSize;

                if (x + desired.Width > finalSize.Width && x > 0)
                {
                    y += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }

                child.Arrange(new Windows.Foundation.Rect(x, y, desired.Width, desired.Height));
                x += desired.Width;
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            return finalSize;
        }
    }
}

// Helper class for processing messages
internal class ProcessedMessage
{
    public string OriginalMessage { get; set; } = "";
    public bool IsSupplementCheck { get; set; }
    public bool IsADASCheck { get; set; }
    public bool IsTrainingQuestion { get; set; }
    public bool IsBlendQuestion { get; set; }
    public bool IsFullOperationEntry { get; set; }
    public bool IsSuggestionRequest { get; set; }
    public bool IsDescriptionBuilderRequest { get; set; }
    public bool IsAdvisorQuery { get; set; }
    public string DetectedPart { get; set; } = "";
    public string DetectedOperation { get; set; } = "";
    public string TrainingTopic { get; set; } = "";
    public List<ParsedOperation> ParsedOperations { get; set; } = new();
}

// Parsed operation from natural language
internal class ParsedOperation
{
    public string OperationType { get; set; } = "";
    public decimal Hours { get; set; }
}

// DEG Inquiry reference
internal class DEGInquiry
{
    public string InquiryNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Response { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
}

// Operation line for CCC paste
internal class OperationLine
{
    public string LineType { get; set; } = ""; // Part, Labor, Refinish, Material
    public string Description { get; set; } = "";
    public string Operation { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal Cost { get; set; }
}

// Chat message for history persistence
internal class ChatMessage
{
    public string Content { get; set; } = "";
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
}

// Chat history data container
internal class ChatHistoryData
{
    public List<ChatMessage> Messages { get; set; } = new();
    public List<int> PinnedIndices { get; set; } = new();
    public DateTime LastSaved { get; set; } = DateTime.Now;
}

// Quick Start category for onboarding
internal class QuickStartCategory
{
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }
    public string[] Examples { get; }

    public QuickStartCategory(string icon, string title, string description, string[] examples)
    {
        Icon = icon;
        Title = title;
        Description = description;
        Examples = examples;
    }
}
