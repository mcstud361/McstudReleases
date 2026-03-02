#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

namespace McStudDesktop.Views;

/// <summary>
/// View for displaying scanning and calibration knowledge.
/// Source: Collision Advise LLC (https://oemonestop.com/)
/// </summary>
public sealed class ScanningCalibrationView : UserControl
{
    private readonly ScanningKnowledgeService _service = ScanningKnowledgeService.Instance;
    private StackPanel? _contentPanel;
    private int _selectedTab = 0;
    private Border? _healthCheckTab;
    private Border? _batteryTab;
    private Border? _oemVsAmTab;
    private Border? _billingTab;

    public ScanningCalibrationView()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18))
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sub-tabs
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // === HEADER ===
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 80, 120)),
            Padding = new Thickness(16, 12, 16, 12)
        };

        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = "Scanning & Calibration Knowledge",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Industry Best Practices | Reference: oemonestop.com",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 220, 255)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // === SUB-TABS ===
        var tabBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 28)),
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var tabStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _healthCheckTab = CreateTabButton("Health Check", 0);
        _batteryTab = CreateTabButton("Battery Support", 1);
        _oemVsAmTab = CreateTabButton("OEM vs Aftermarket", 2);
        _billingTab = CreateTabButton("Billing Questions", 3);

        tabStack.Children.Add(_healthCheckTab);
        tabStack.Children.Add(_batteryTab);
        tabStack.Children.Add(_oemVsAmTab);
        tabStack.Children.Add(_billingTab);

        tabBar.Child = tabStack;
        Grid.SetRow(tabBar, 1);
        mainGrid.Children.Add(tabBar);

        // === CONTENT ===
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16)
        };

        _contentPanel = new StackPanel { Spacing = 12 };
        scrollViewer.Content = _contentPanel;

        Grid.SetRow(scrollViewer, 2);
        mainGrid.Children.Add(scrollViewer);

        Content = mainGrid;

        // Load initial content
        SelectTab(0);
    }

    private Border CreateTabButton(string text, int tabIndex)
    {
        bool isSelected = tabIndex == _selectedTab;

        var border = new Border
        {
            Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(255, 0, 80, 120)
                : Color.FromArgb(255, 45, 45, 45)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6, 12, 6)
        };

        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(255, 180, 180, 180))
        };

        border.Child = label;
        border.PointerPressed += (s, e) => SelectTab(tabIndex);
        border.PointerEntered += (s, e) =>
        {
            if (tabIndex != _selectedTab)
                border.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
        };
        border.PointerExited += (s, e) =>
        {
            if (tabIndex != _selectedTab)
                border.Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
        };

        return border;
    }

    private void UpdateTabStyle(Border? tab, int index)
    {
        if (tab == null) return;
        bool isSelected = index == _selectedTab;

        tab.Background = new SolidColorBrush(isSelected
            ? Color.FromArgb(255, 0, 80, 120)
            : Color.FromArgb(255, 45, 45, 45));

        if (tab.Child is TextBlock label)
        {
            label.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            label.Foreground = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(255, 180, 180, 180));
        }
    }

    private void SelectTab(int tabIndex)
    {
        _selectedTab = tabIndex;

        UpdateTabStyle(_healthCheckTab, 0);
        UpdateTabStyle(_batteryTab, 1);
        UpdateTabStyle(_oemVsAmTab, 2);
        UpdateTabStyle(_billingTab, 3);

        _contentPanel?.Children.Clear();

        switch (tabIndex)
        {
            case 0: BuildHealthCheckContent(); break;
            case 1: BuildBatterySupportContent(); break;
            case 2: BuildOemVsAftermarketContent(); break;
            case 3: BuildBillingContent(); break;
        }
    }

    private void BuildHealthCheckContent()
    {
        if (_contentPanel == null) return;

        var procedure = _service.GetHealthCheckProcedure();

        // Info card
        var infoCard = CreateCard("Health Check Procedure", Color.FromArgb(255, 0, 100, 70));
        var infoStack = (StackPanel)infoCard.Tag!;
        infoStack.Children.Add(new TextBlock
        {
            Text = $"Base Labor: {procedure.BaseLabor}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 180))
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = procedure.BaseLaborDescription ?? "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _contentPanel.Children.Add(infoCard);

        // Included steps
        var includedCard = CreateCard("INCLUDED in 0.5m Base", Color.FromArgb(255, 0, 120, 80));
        var includedStack = (StackPanel)includedCard.Tag!;
        foreach (var step in _service.GetIncludedSteps())
        {
            includedStack.Children.Add(CreateStepItem(step, true));
        }
        _contentPanel.Children.Add(includedCard);

        // Not included steps
        var notIncludedCard = CreateCard("NOT INCLUDED - Bill Separately", Color.FromArgb(255, 180, 100, 0));
        var notIncludedStack = (StackPanel)notIncludedCard.Tag!;
        foreach (var step in _service.GetNotIncludedSteps())
        {
            notIncludedStack.Children.Add(CreateStepItem(step, false));
        }
        _contentPanel.Children.Add(notIncludedCard);

        // Calibration types
        var calibCard = CreateCard("Calibration Types (NOT INCLUDED)", Color.FromArgb(255, 100, 80, 140));
        var calibStack = (StackPanel)calibCard.Tag!;
        foreach (var type in _service.GetCalibrationTypes())
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            item.Children.Add(new TextBlock
            {
                Text = $"{type.Type}:",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                Width = 180
            });
            item.Children.Add(new TextBlock
            {
                Text = type.Description ?? "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });
            calibStack.Children.Add(item);
        }
        _contentPanel.Children.Add(calibCard);
    }

    private UIElement CreateStepItem(HealthCheckStep step, bool isIncluded)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = isIncluded ? "\u2705" : "\u2795", // Checkmark or Plus
            FontSize = 12,
            Margin = new Thickness(0, 0, 8, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{step.Step}. {step.Description}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(header);

        if (!string.IsNullOrEmpty(step.Notes))
        {
            stack.Children.Add(new TextBlock
            {
                Text = step.Notes,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                Margin = new Thickness(24, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (step.Examples != null && step.Examples.Count > 0)
        {
            var examplesText = string.Join(", ", step.Examples);
            stack.Children.Add(new TextBlock
            {
                Text = $"Examples: {examplesText}",
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
                Margin = new Thickness(24, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return stack;
    }

    private void BuildBatterySupportContent()
    {
        if (_contentPanel == null) return;

        var info = _service.GetBatterySupportInfo();

        // Warning card
        var warningCard = CreateCard("NOT Battery Support", Color.FromArgb(255, 180, 50, 50));
        var warningStack = (StackPanel)warningCard.Tag!;
        foreach (var item in info.CriticalInfo?.NotBatterySupport ?? new List<string>())
        {
            warningStack.Children.Add(new TextBlock
            {
                Text = $"\u274C {item}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 150, 150)),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        warningStack.Children.Add(new TextBlock
        {
            Text = "\nWhy NOT:",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 8, 0, 0)
        });

        foreach (var reason in info.CriticalInfo?.Reasons ?? new List<string>())
        {
            warningStack.Children.Add(new TextBlock
            {
                Text = $"• {reason}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4, 0, 0)
            });
        }
        _contentPanel.Children.Add(warningCard);

        // Best choice card
        var bestCard = CreateCard("Recommended Battery Support", Color.FromArgb(255, 0, 120, 80));
        var bestStack = (StackPanel)bestCard.Tag!;
        bestStack.Children.Add(new TextBlock
        {
            Text = $"\u2705 {info.CriticalInfo?.BestChoice}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 255, 180))
        });
        bestStack.Children.Add(new TextBlock
        {
            Text = $"OEM Recommended: {info.CriticalInfo?.OemRecommended}",
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 8, 0, 0)
        });
        _contentPanel.Children.Add(bestCard);

        // Why it matters
        var whyCard = CreateCard("Why Battery Support Matters", Color.FromArgb(255, 80, 80, 140));
        var whyStack = (StackPanel)whyCard.Tag!;
        foreach (var why in info.WhyBatterySupportMatters ?? new List<string>())
        {
            whyStack.Children.Add(new TextBlock
            {
                Text = $"• {why}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }
        _contentPanel.Children.Add(whyCard);

        // DTC Colors
        var colorsCard = CreateCard("DTC Code Status Colors (OEM Tools)", Color.FromArgb(255, 60, 60, 80));
        var colorsStack = (StackPanel)colorsCard.Tag!;
        foreach (var c in _service.GetDtcCodeColors())
        {
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

            var colorBrush = c.Color switch
            {
                "Red" => new SolidColorBrush(Color.FromArgb(255, 255, 80, 80)),
                "Yellow" => new SolidColorBrush(Color.FromArgb(255, 255, 220, 80)),
                "Green" => new SolidColorBrush(Color.FromArgb(255, 80, 220, 80)),
                _ => new SolidColorBrush(Colors.White)
            };

            colorRow.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = colorBrush,
                Margin = new Thickness(0, 0, 10, 0)
            });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = $"{c.Color}: {c.Meaning}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = colorBrush
            });
            textStack.Children.Add(new TextBlock
            {
                Text = c.Action ?? "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            });
            colorRow.Children.Add(textStack);

            colorsStack.Children.Add(colorRow);
        }
        _contentPanel.Children.Add(colorsCard);
    }

    private void BuildOemVsAftermarketContent()
    {
        if (_contentPanel == null) return;

        // Comparison table
        var compCard = CreateCard("OEM vs Aftermarket Scan Tools Comparison", Color.FromArgb(255, 0, 80, 120));
        var compStack = (StackPanel)compCard.Tag!;

        foreach (var item in _service.GetOemVsAftermarketComparison())
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 35)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var itemStack = new StackPanel();
            itemStack.Children.Add(new TextBlock
            {
                Text = item.Category ?? "",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
            });

            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var oemStack = new StackPanel();
            oemStack.Children.Add(new TextBlock
            {
                Text = "OEM",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 100))
            });
            oemStack.Children.Add(new TextBlock
            {
                Text = item.Oem ?? "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(oemStack, 0);
            row.Children.Add(oemStack);

            var amStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            amStack.Children.Add(new TextBlock
            {
                Text = "Aftermarket",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 180, 100))
            });
            amStack.Children.Add(new TextBlock
            {
                Text = item.Aftermarket ?? "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(amStack, 1);
            row.Children.Add(amStack);

            itemStack.Children.Add(row);
            itemBorder.Child = itemStack;
            compStack.Children.Add(itemBorder);
        }
        _contentPanel.Children.Add(compCard);

        // Questions to ask
        var questionsCard = CreateCard("Questions to Ask Aftermarket Vendors", Color.FromArgb(255, 140, 100, 0));
        var questionsStack = (StackPanel)questionsCard.Tag!;
        int qNum = 1;
        foreach (var q in _service.GetAftermarketQuestions())
        {
            questionsStack.Children.Add(new TextBlock
            {
                Text = $"{qNum}. {q}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            qNum++;
        }
        _contentPanel.Children.Add(questionsCard);
    }

    private void BuildBillingContent()
    {
        if (_contentPanel == null) return;

        // Four Key Questions
        var questionsCard = CreateCard("Four Key Questions for Billing", Color.FromArgb(255, 0, 100, 180));
        var questionsStack = (StackPanel)questionsCard.Tag!;
        questionsStack.Children.Add(new TextBlock
        {
            Text = "Constantly ask yourself these questions:",
            FontSize = 12,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var q in _service.GetFourKeyQuestions())
        {
            var qBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 50, 70)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var qStack = new StackPanel { Orientation = Orientation.Horizontal };
            qStack.Children.Add(new TextBlock
            {
                Text = $"{q.Number}.",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)),
                Width = 30
            });
            qStack.Children.Add(new TextBlock
            {
                Text = q.Question ?? "",
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            qBorder.Child = qStack;
            questionsStack.Children.Add(qBorder);
        }
        _contentPanel.Children.Add(questionsCard);

        // Scanning Myths
        var myths = _service.GetScanningMyths();
        var mythCard = CreateCard("Myth: \"Insurance Only Wants to Pay $50 for a Scan\"", Color.FromArgb(255, 180, 80, 0));
        var mythStack = (StackPanel)mythCard.Tag!;

        mythStack.Children.Add(new TextBlock
        {
            Text = "How to Respond:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var r in myths.Responses ?? new List<MythResponse>())
        {
            mythStack.Children.Add(new TextBlock
            {
                Text = $"{r.Step}. {r.Action}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            if (r.Examples != null && r.Examples.Count > 0)
            {
                foreach (var ex in r.Examples)
                {
                    mythStack.Children.Add(new TextBlock
                    {
                        Text = $"   • {ex}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                        Margin = new Thickness(12, 2, 0, 0)
                    });
                }
            }
        }
        _contentPanel.Children.Add(mythCard);

        // Resource link
        var resourceCard = CreateCard("Resources", Color.FromArgb(255, 60, 60, 80));
        var resourceStack = (StackPanel)resourceCard.Tag!;
        var resource = _service.GetOemResource();
        resourceStack.Children.Add(new TextBlock
        {
            Text = $"{resource.Name}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255))
        });
        resourceStack.Children.Add(new TextBlock
        {
            Text = resource.Url ?? "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        resourceStack.Children.Add(new TextBlock
        {
            Text = resource.Description ?? "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _contentPanel.Children.Add(resourceCard);
    }

    private Border CreateCard(string title, Color headerColor)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 28)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 50, 50, 55)),
            BorderThickness = new Thickness(1)
        };

        var outerStack = new StackPanel();

        // Header
        var header = new Border
        {
            Background = new SolidColorBrush(headerColor),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(5, 5, 0, 0)
        };
        header.Child = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        outerStack.Children.Add(header);

        // Content area (will be populated by caller)
        var contentArea = new StackPanel { Padding = new Thickness(12) };
        outerStack.Children.Add(contentArea);

        card.Child = outerStack;

        // Caller needs to get contentArea - we'll use Tag
        card.Tag = contentArea;

        return card;
    }
}
