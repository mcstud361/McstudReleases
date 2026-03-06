#nullable enable
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using McStudDesktop.Services;

namespace McStudDesktop.Views
{
    /// <summary>
    /// MET Guide View - Interactive guide for using MET Excel tool
    /// Features expandable sections, search, and step-by-step instructions.
    /// </summary>
    public sealed class METGuideView : UserControl
    {
        private readonly METGuideService _guideService;

        // UI Elements
        private TextBox? _searchBox;
        private StackPanel? _contentStack;
        private StackPanel? _searchResultsStack;
        private Border? _searchResultsContainer;
        private TextBlock? _noResultsText;

        // Colors
        private static readonly Color DarkBg = Color.FromArgb(255, 18, 18, 18);
        private static readonly Color CardBg = Color.FromArgb(255, 28, 28, 28);
        private static readonly Color SectionBg = Color.FromArgb(255, 35, 35, 35);
        private static readonly Color AccentBlue = Color.FromArgb(255, 0, 120, 215);
        private static readonly Color AccentGreen = Color.FromArgb(255, 0, 150, 80);
        private static readonly Color AccentOrange = Color.FromArgb(255, 255, 150, 0);
        private static readonly Color TextGray = Color.FromArgb(255, 160, 160, 160);

        // State
        private Dictionary<string, Expander> _sectionExpanders = new();
        private Dictionary<string, StackPanel> _topicPanels = new();

        public METGuideView()
        {
            _guideService = METGuideService.Instance;
            BuildUI();
        }

        private void BuildUI()
        {
            var rootBorder = new Border
            {
                Background = new SolidColorBrush(DarkBg)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };

            var mainStack = new StackPanel
            {
                Spacing = 12,
                Padding = new Thickness(12)
            };

            // === HEADER ===
            mainStack.Children.Add(BuildHeader());

            // === SEARCH BOX ===
            mainStack.Children.Add(BuildSearchSection());

            // === SEARCH RESULTS (hidden by default) ===
            _searchResultsContainer = BuildSearchResultsContainer();
            mainStack.Children.Add(_searchResultsContainer);

            // === QUICK ANSWERS ===
            mainStack.Children.Add(BuildQuickAnswersSection());

            // === GUIDE SECTIONS ===
            _contentStack = new StackPanel { Spacing = 8 };
            BuildGuideSections();
            mainStack.Children.Add(_contentStack);

            scrollViewer.Content = mainStack;
            rootBorder.Child = scrollViewer;
            Content = rootBorder;
        }

        private Grid BuildHeader()
        {
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            iconStack.Children.Add(new FontIcon
            {
                Glyph = "\uE82D", // Book icon
                FontSize = 22,
                Foreground = new SolidColorBrush(AccentBlue)
            });

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "MET Guide",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Learn how to use MET Excel tool",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextGray)
            });

            iconStack.Children.Add(titleStack);
            Grid.SetColumn(iconStack, 0);
            headerGrid.Children.Add(iconStack);

            return headerGrid;
        }

        private Border BuildSearchSection()
        {
            var searchBorder = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10)
            };

            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchIcon = new FontIcon
            {
                Glyph = "\uE721",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextGray),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                PlaceholderText = "Search guide... (e.g., \"paste\", \"input cell\", \"tabs\")",
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            };
            _searchBox.TextChanged += OnSearchTextChanged;
            Grid.SetColumn(_searchBox, 1);

            searchGrid.Children.Add(searchIcon);
            searchGrid.Children.Add(_searchBox);

            searchBorder.Child = searchGrid;
            return searchBorder;
        }

        private Border BuildSearchResultsContainer()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel { Spacing = 8 };

            var header = new TextBlock
            {
                Text = "Search Results",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(header);

            _searchResultsStack = new StackPanel { Spacing = 6 };
            stack.Children.Add(_searchResultsStack);

            _noResultsText = new TextBlock
            {
                Text = "No results found",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextGray),
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(_noResultsText);

            border.Child = stack;
            return border;
        }

        private Border BuildQuickAnswersSection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10)
            };

            var expander = new Expander
            {
                Header = CreateExpanderHeader("Quick Answers", "\uE897", "Common questions answered instantly"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            var contentStack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

            var quickAnswers = _guideService.GetQuickAnswers();
            foreach (var qa in quickAnswers.Take(8))
            {
                var qaPanel = CreateQuickAnswerItem(qa.Key, qa.Value);
                contentStack.Children.Add(qaPanel);
            }

            expander.Content = contentStack;
            border.Child = expander;
            return border;
        }

        private Border CreateQuickAnswerItem(string question, string answer)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var stack = new StackPanel { Spacing = 4 };

            var questionText = new TextBlock
            {
                Text = $"Q: {char.ToUpper(question[0])}{question.Substring(1)}?",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentOrange),
                TextWrapping = TextWrapping.Wrap
            };

            var answerText = new TextBlock
            {
                Text = $"A: {answer}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap
            };

            stack.Children.Add(questionText);
            stack.Children.Add(answerText);

            border.Child = stack;
            return border;
        }

        private void BuildGuideSections()
        {
            if (_contentStack == null) return;

            var sections = _guideService.GetSections();

            foreach (var section in sections)
            {
                var sectionBorder = BuildSectionExpander(section);
                _contentStack.Children.Add(sectionBorder);
            }
        }

        private Border BuildSectionExpander(GuideSection section)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10)
            };

            var expander = new Expander
            {
                Header = CreateExpanderHeader(
                    section.Title ?? "Section",
                    $"\\u{section.Icon ?? "E8F1"}",
                    $"{section.Topics?.Count ?? 0} topics"
                ),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };

            var contentStack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

            if (section.Topics != null)
            {
                foreach (var topic in section.Topics)
                {
                    var topicPanel = BuildTopicPanel(topic);
                    contentStack.Children.Add(topicPanel);

                    if (topic.Id != null)
                    {
                        _topicPanels[topic.Id] = topicPanel;
                    }
                }
            }

            expander.Content = contentStack;

            if (section.Id != null)
            {
                _sectionExpanders[section.Id] = expander;
            }

            border.Child = expander;
            return border;
        }

        private StackPanel CreateExpanderHeader(string title, string glyph, string subtitle)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            // Try to parse the glyph
            string iconGlyph = "\uE8F1"; // Default
            if (glyph.StartsWith("\\u") && glyph.Length >= 6)
            {
                try
                {
                    iconGlyph = char.ConvertFromUtf32(Convert.ToInt32(glyph.Substring(2), 16));
                }
                catch { }
            }
            else if (glyph.Length == 4)
            {
                try
                {
                    iconGlyph = char.ConvertFromUtf32(Convert.ToInt32(glyph, 16));
                }
                catch { }
            }

            stack.Children.Add(new FontIcon
            {
                Glyph = iconGlyph,
                FontSize = 16,
                Foreground = new SolidColorBrush(AccentBlue)
            });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray)
            });

            stack.Children.Add(textStack);
            return stack;
        }

        private StackPanel BuildTopicPanel(GuideTopic topic)
        {
            var panel = new StackPanel { Spacing = 6 };

            // Topic header
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var headerStack = new StackPanel { Spacing = 4 };

            headerStack.Children.Add(new TextBlock
            {
                Text = topic.Title ?? "Topic",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            if (!string.IsNullOrEmpty(topic.Summary))
            {
                headerStack.Children.Add(new TextBlock
                {
                    Text = topic.Summary,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextGray),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            // Images (if any)
            if (topic.Images != null && topic.Images.Count > 0)
            {
                var imageStack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
                foreach (var imageName in topic.Images)
                {
                    try
                    {
                        var imagePath = System.IO.Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "Assets", "GuideImages", imageName);

                        if (System.IO.File.Exists(imagePath))
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imagePath));
                            var image = new Image
                            {
                                Source = bitmap,
                                MaxHeight = 150,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                            };

                            var imageBorder = new Border
                            {
                                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(4),
                                Child = image
                            };

                            imageStack.Children.Add(imageBorder);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[METGuide] Error loading image {imageName}: {ex.Message}");
                    }
                }
                if (imageStack.Children.Count > 0)
                {
                    headerStack.Children.Add(imageStack);
                }
            }

            // Content
            if (topic.Content != null && topic.Content.Count > 0)
            {
                var contentStack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
                foreach (var line in topic.Content)
                {
                    var bullet = line.StartsWith("Step") || line.StartsWith("1") || line.StartsWith("2") ||
                                 line.StartsWith("3") || line.StartsWith("4") || line.StartsWith("-")
                        ? "" : "• ";

                    contentStack.Children.Add(new TextBlock
                    {
                        Text = bullet + line,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.White),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 0, 0, 0)
                    });
                }
                headerStack.Children.Add(contentStack);
            }

            // Tips
            if (topic.Tips != null && topic.Tips.Count > 0)
            {
                var tipsHeader = new TextBlock
                {
                    Text = "Tips:",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentGreen),
                    Margin = new Thickness(0, 8, 0, 2)
                };
                headerStack.Children.Add(tipsHeader);

                foreach (var tip in topic.Tips)
                {
                    headerStack.Children.Add(new TextBlock
                    {
                        Text = "✓ " + tip,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(AccentGreen),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 0, 0, 0)
                    });
                }
            }

            // Warnings
            if (topic.Warnings != null && topic.Warnings.Count > 0)
            {
                var warningsHeader = new TextBlock
                {
                    Text = "Warnings:",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentOrange),
                    Margin = new Thickness(0, 8, 0, 2)
                };
                headerStack.Children.Add(warningsHeader);

                foreach (var warning in topic.Warnings)
                {
                    headerStack.Children.Add(new TextBlock
                    {
                        Text = "⚠ " + warning,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(AccentOrange),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 0, 0, 0)
                    });
                }
            }

            // Read Aloud button
            var readAloudRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var readAloudBtn = Services.TextToSpeechService.CreateReadAloudButton(() =>
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(topic.Title)) sb.AppendLine(topic.Title);
                if (!string.IsNullOrEmpty(topic.Summary)) sb.AppendLine(topic.Summary);
                if (topic.Content?.Count > 0) { foreach (var line in topic.Content) sb.AppendLine(line); }
                if (topic.Tips?.Count > 0) { sb.AppendLine("Tips:"); foreach (var tip in topic.Tips) sb.AppendLine(tip); }
                if (topic.Warnings?.Count > 0) { sb.AppendLine("Warnings:"); foreach (var w in topic.Warnings) sb.AppendLine(w); }
                return sb.ToString();
            });
            readAloudRow.Children.Add(readAloudBtn);
            headerStack.Children.Add(readAloudRow);

            headerBorder.Child = headerStack;
            panel.Children.Add(headerBorder);

            return panel;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = _searchBox?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(query))
            {
                if (_searchResultsContainer != null)
                    _searchResultsContainer.Visibility = Visibility.Collapsed;
                return;
            }

            var results = _guideService.Search(query);

            if (_searchResultsStack == null || _searchResultsContainer == null || _noResultsText == null)
                return;

            _searchResultsStack.Children.Clear();
            _searchResultsContainer.Visibility = Visibility.Visible;

            if (results.Count == 0)
            {
                _noResultsText.Visibility = Visibility.Visible;
                return;
            }

            _noResultsText.Visibility = Visibility.Collapsed;

            foreach (var result in results.Take(5))
            {
                var resultItem = CreateSearchResultItem(result);
                _searchResultsStack.Children.Add(resultItem);
            }
        }

        private Border CreateSearchResultItem(GuideSearchResult result)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SectionBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var stack = new StackPanel { Spacing = 2 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            titleRow.Children.Add(new TextBlock
            {
                Text = result.Topic?.Title ?? "",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = $"({result.SectionTitle})",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextGray),
                VerticalAlignment = VerticalAlignment.Center
            });

            stack.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(result.Topic?.Summary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = result.Topic.Summary,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextGray),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            border.Child = stack;

            // Click to expand section
            border.PointerPressed += (s, e) =>
            {
                // Expand the section containing this topic
                if (!string.IsNullOrEmpty(result.SectionId) &&
                    _sectionExpanders.TryGetValue(result.SectionId, out var expander))
                {
                    expander.IsExpanded = true;
                }

                // Clear search
                if (_searchBox != null)
                {
                    _searchBox.Text = "";
                }
            };

            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(SectionBg);
            };

            return border;
        }

        /// <summary>
        /// Navigate to a specific topic (for contextual help links)
        /// </summary>
        public void NavigateToTopic(string topicId)
        {
            // Find the section containing this topic
            var sections = _guideService.GetSections();
            foreach (var section in sections)
            {
                if (section.Topics?.Any(t => t.Id == topicId) == true)
                {
                    if (section.Id != null && _sectionExpanders.TryGetValue(section.Id, out var expander))
                    {
                        expander.IsExpanded = true;
                    }
                    break;
                }
            }
        }
    }
}
