#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using McStudDesktop.Services;
using McstudDesktop;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace McStudDesktop.Views
{
    /// <summary>
    /// Learning Center - Upload estimates, view stats, query learned data
    /// </summary>
    public sealed class LearningCenterView : UserControl
    {
        private TextBox _queryInput = null!;
        private TextBlock _queryResult = null!;
        private TextBlock _statsText = null!;
        private ListView _patternsListView = null!;
        private ListView _parsedItemsListView = null!;
        private TextBlock _importStatusText = null!;
        private ProgressRing _importProgress = null!;

        public LearningCenterView()
        {
            BuildUI();
            RefreshStats();
        }

        private void BuildUI()
        {
            var mainGrid = new Grid
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 28)),
                Padding = new Thickness(20)
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var header = new TextBlock
            {
                Text = "Learning Center",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Main content with tabs
            var tabView = new TabView
            {
                TabWidthMode = TabViewWidthMode.SizeToContent,
                IsAddTabButtonVisible = false,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35))
            };

            // Tab 1: Query Operations
            var queryTab = new TabViewItem
            {
                Header = "Query Operations",
                Content = CreateQueryPanel()
            };
            tabView.TabItems.Add(queryTab);

            // Tab 2: Import Estimates
            var importTab = new TabViewItem
            {
                Header = "Import Estimates",
                Content = CreateImportPanel()
            };
            tabView.TabItems.Add(importTab);

            // Tab 3: Learning Stats
            var statsTab = new TabViewItem
            {
                Header = "Statistics",
                Content = CreateStatsPanel()
            };
            tabView.TabItems.Add(statsTab);

            // Tab 4: Patterns Database
            var patternsTab = new TabViewItem
            {
                Header = "Patterns Database",
                Content = CreatePatternsPanel()
            };
            tabView.TabItems.Add(patternsTab);

            // Tab 5: Tips & Tricks
            var tipsTab = new TabViewItem
            {
                Header = "Tips & Tricks",
                Content = CreateTipsPanel()
            };
            tabView.TabItems.Add(tipsTab);

            Grid.SetRow(tabView, 1);
            mainGrid.Children.Add(tabView);

            Content = mainGrid;
        }

        private UIElement CreateQueryPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Instructions
            panel.Children.Add(new TextBlock
            {
                Text = "Ask questions about operations based on learned estimates",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                FontSize = 14
            });

            // Query input
            var inputStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            _queryInput = new TextBox
            {
                PlaceholderText = "Example: I'm repairing a door, what operations do I need?",
                Width = 500,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            inputStack.Children.Add(_queryInput);

            var queryButton = new Button
            {
                Content = "Search",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            queryButton.Click += QueryButton_Click;
            inputStack.Children.Add(queryButton);

            panel.Children.Add(inputStack);

            // Quick query buttons
            var quickStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            quickStack.Children.Add(new TextBlock
            {
                Text = "Quick:",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
            });

            var parts = new[] { "Bumper", "Fender", "Door", "Hood", "Quarter Panel" };
            foreach (var part in parts)
            {
                var btn = new Button
                {
                    Content = part,
                    Tag = part,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    Padding = new Thickness(10, 5, 10, 5)
                };
                btn.Click += QuickQuery_Click;
                quickStack.Children.Add(btn);
            }
            panel.Children.Add(quickStack);

            // Results area
            _queryResult = new TextBlock
            {
                Text = "Enter a query above to search learned operations...",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            };
            panel.Children.Add(_queryResult);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private UIElement CreateImportPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Instructions
            panel.Children.Add(new TextBlock
            {
                Text = "Import estimate PDFs to train the learning system",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                FontSize = 14
            });

            // Import button
            var importStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            var importButton = new Button
            {
                Content = "Select PDF to Import",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(20, 10, 20, 10)
            };
            importButton.Click += ImportButton_Click;
            importStack.Children.Add(importButton);

            _importProgress = new ProgressRing
            {
                IsActive = false,
                Width = 24,
                Height = 24
            };
            importStack.Children.Add(_importProgress);

            panel.Children.Add(importStack);

            // Status text
            _importStatusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_importStatusText);

            // Parsed items preview
            panel.Children.Add(new TextBlock
            {
                Text = "Parsed Line Items:",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 5)
            });

            _parsedItemsListView = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                MaxHeight = 300,
                SelectionMode = ListViewSelectionMode.None
            };
            panel.Children.Add(_parsedItemsListView);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private TextBlock? _miningStatusText;
        private ProgressRing? _miningProgress;

        private UIElement CreateStatsPanel()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(20),
                Spacing = 15
            };

            // Stats display
            _statsText = new TextBlock
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_statsText);

            // Mining section header
            var miningHeader = new TextBlock
            {
                Text = "PATTERN MINING ENGINE",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255)),
                Margin = new Thickness(0, 30, 0, 10)
            };
            panel.Children.Add(miningHeader);

            var miningDesc = new TextBlock
            {
                Text = "Run a full mining pass to analyze ALL uploaded estimates and extract patterns.\nThis improves ghost estimates, part recognition, and suggestions.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(miningDesc);

            // Mining status and progress
            var miningStatusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            _miningProgress = new ProgressRing
            {
                Width = 20,
                Height = 20,
                IsActive = false,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255))
            };
            miningStatusPanel.Children.Add(_miningProgress);

            _miningStatusText = new TextBlock
            {
                Text = "Ready",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            miningStatusPanel.Children.Add(_miningStatusText);
            panel.Children.Add(miningStatusPanel);

            // Run Mining Pass button
            var miningButton = new Button
            {
                Content = "Run Full Mining Pass",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 100, 140)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 10, 0, 0)
            };
            miningButton.Click += RunMiningPass_Click;
            panel.Children.Add(miningButton);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                Margin = new Thickness(0, 20, 0, 10)
            };
            panel.Children.Add(separator);

            // Button row
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            // Refresh button
            var refreshButton = new Button
            {
                Content = "Refresh Statistics",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
            };
            refreshButton.Click += (s, e) => RefreshStats();
            buttonRow.Children.Add(refreshButton);

            // Clear data button (dangerous)
            var clearButton = new Button
            {
                Content = "Clear All Learned Data",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 50, 50)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 200))
            };
            clearButton.Click += ClearData_Click;
            buttonRow.Children.Add(clearButton);

            panel.Children.Add(buttonRow);

            return new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private async void RunMiningPass_Click(object sender, RoutedEventArgs e)
        {
            if (EstimateMiningEngine.Instance.IsMining)
            {
                _miningStatusText!.Text = "Mining already in progress...";
                return;
            }

            _miningProgress!.IsActive = true;
            _miningStatusText!.Text = "Starting mining pass...";
            _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255));

            // Subscribe to progress updates
            EstimateMiningEngine.Instance.ProgressChanged += (s, progress) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _miningStatusText.Text = $"{progress.Phase}: {progress.ProcessedEstimates}/{progress.TotalEstimates} ({progress.PercentComplete:F0}%)";
                });
            };

            try
            {
                var result = await EstimateMiningEngine.Instance.RunFullMiningPassAsync();

                _miningProgress.IsActive = false;
                if (result.Success)
                {
                    _miningStatusText.Text = $"Complete! Learned {result.PartsLearned} parts, {result.PatternsDiscovered} patterns in {result.Duration.TotalSeconds:F1}s";
                    _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 255, 100));
                }
                else
                {
                    _miningStatusText.Text = $"Failed: {result.Message}";
                    _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
                }

                // Refresh stats to show new data
                RefreshStats();
            }
            catch (Exception ex)
            {
                _miningProgress.IsActive = false;
                _miningStatusText.Text = $"Error: {ex.Message}";
                _miningStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
            }
        }

        private UIElement CreatePatternsPanel()
        {
            var panel = new Grid
            {
                Padding = new Thickness(20)
            };

            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            var headerText = new TextBlock
            {
                Text = "Learned Patterns (sorted by confidence)",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(headerText, 0);
            panel.Children.Add(headerText);

            // Patterns list
            _patternsListView = new ListView
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                SelectionMode = ListViewSelectionMode.None
            };
            Grid.SetRow(_patternsListView, 1);
            panel.Children.Add(_patternsListView);

            return panel;
        }

        private UIElement CreateTipsPanel()
        {
            var mainScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20)
            };

            var panel = new StackPanel { Spacing = 20 };

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = "Estimating Tips & Tricks",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130))
            });

            // Photo Documentation Section
            panel.Children.Add(CreateTipSection(
                "Photo Documentation: Telling the Story to Insurers",
                "By Anthony McNee",
                new List<TipItem>
                {
                    new TipItem
                    {
                        Title = "Photos as Visual Illustrations",
                        Content = "Photos should TELL A STORY, not just document. Anyone reviewing them (especially adjusters) should instantly understand what repairs are needed and their severity.",
                        Icon = "\uE722" // Camera
                    },
                    new TipItem
                    {
                        Title = "Lighting is Critical",
                        Content = "Proper lighting reveals damage depth and detail that might otherwise be missed entirely. Poor lighting = missed damage = reduced labor hours. Position lights to show dents, scratches, and texture damage clearly.",
                        Icon = "\uE706" // Brightness
                    },
                    new TipItem
                    {
                        Title = "Perspective & Context",
                        Content = "View photos from the adjuster's perspective - they may not know where a part originated. TIP: Put the removed part next to where you took it off the vehicle to show scale and location clearly.",
                        Icon = "\uE8B9" // View
                    },
                    new TipItem
                    {
                        Title = "Sequential Documentation",
                        Content = "Photos should follow a logical progression. Create a coherent narrative: Overall damage → Close-up details → Hidden damage → Repair process → Completed work.",
                        Icon = "\uE8FD" // List
                    },
                    new TipItem
                    {
                        Title = "Use Visual Aid Tools",
                        Content = "Use dent boards for scale comparison. Use COLOR-CODED POINTER STICKS:\n• One color for REPAIR operations\n• Different color for REPLACE operations\nThis makes it instantly clear what work is required.",
                        Icon = "\uE790" // Highlight
                    },
                    new TipItem
                    {
                        Title = "Document Before Disassembly",
                        Content = "Always photograph damage BEFORE removing parts. Once disassembled, the original damage context is lost. Take wide shots and close-ups of each damaged area.",
                        Icon = "\uE7C5" // Shield
                    },
                    new TipItem
                    {
                        Title = "Get ALL Info While Vehicle is Present",
                        Content = "Don't let the car leave without capturing EVERYTHING:\n• Check for ADAS equipment (cameras, sensors, radar)\n• Walk around entire vehicle for prior/unrelated damage\n• Get it on the measuring system\n• Check and document the paint code\n• Verify VIN and options\n• Check tire sizes and condition\n• Look for aftermarket parts/modifications\n\nYou can't supplement what you didn't document!",
                        Icon = "\uE762" // Car
                    }
                }
            ));

            // Photo Checklist
            panel.Children.Add(CreateChecklistSection(
                "Photo Documentation Checklist",
                new List<string>
                {
                    "Overall vehicle photos (all 4 corners)",
                    "VIN plate / sticker photo",
                    "Odometer reading",
                    "Paint code label photo",
                    "Wide shot of damaged area",
                    "Close-up of each damage point",
                    "Measuring tape/scale reference for dents",
                    "Photos with dent board behind damage",
                    "Color-coded pointers indicating repair vs replace",
                    "Hidden damage revealed during teardown",
                    "Part numbers on OEM parts being replaced",
                    "Before/After comparison shots",
                    "ADAS components that need calibration"
                }
            ));

            // Vehicle Inspection Checklist
            panel.Children.Add(CreateChecklistSection(
                "While Vehicle is Present - Don't Miss!",
                new List<string>
                {
                    "Check ALL ADAS equipment (cameras, radar, sensors)",
                    "Walk around ENTIRE vehicle for prior/unrelated damage",
                    "Get vehicle on measuring system",
                    "Document paint code from label",
                    "Verify VIN matches paperwork",
                    "Check tire sizes and condition",
                    "Note any aftermarket parts or modifications",
                    "Check for fluid leaks",
                    "Test all lights and electrical",
                    "Document interior condition",
                    "Check for warning lights on dash",
                    "Note any unusual sounds or smells"
                }
            ));

            // Color Coding Guide
            panel.Children.Add(CreateColorGuideSection());

            // More Tips Coming Soon
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 50, 45)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = "More tips coming soon! Have a tip to share? Let us know.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 200, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                }
            });

            mainScroll.Content = panel;
            return mainScroll;
        }

        private Border CreateTipSection(string title, string author, List<TipItem> tips)
        {
            var sectionPanel = new StackPanel { Spacing = 12 };

            // Section header
            var headerStack = new StackPanel { Spacing = 4 };
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = author,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 180, 130)),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
            sectionPanel.Children.Add(headerStack);

            // Tips
            foreach (var tip in tips)
            {
                var tipCard = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12)
                };

                var tipGrid = new Grid();
                tipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                tipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Icon
                var icon = new FontIcon
                {
                    Glyph = tip.Icon,
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130)),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(icon, 0);
                tipGrid.Children.Add(icon);

                // Content
                var contentStack = new StackPanel { Spacing = 4 };
                contentStack.Children.Add(new TextBlock
                {
                    Text = tip.Title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 220, 100))
                });
                contentStack.Children.Add(new TextBlock
                {
                    Text = tip.Content,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(contentStack, 1);
                tipGrid.Children.Add(contentStack);

                tipCard.Child = tipGrid;
                sectionPanel.Children.Add(tipCard);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = sectionPanel
            };
        }

        private Border CreateChecklistSection(string title, List<string> items)
        {
            var panel = new StackPanel { Spacing = 8 };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var item in items)
            {
                var itemStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                itemStack.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E", // Checkmark
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 130))
                });
                itemStack.Children.Add(new TextBlock
                {
                    Text = item,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
                });
                panel.Children.Add(itemStack);
            }

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = panel
            };
        }

        private Border CreateColorGuideSection()
        {
            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(new TextBlock
            {
                Text = "Pointer Stick Color Coding Guide",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Color guide items
            var colors = new List<(string color, string hex, string meaning)>
            {
                ("RED", "#FF4444", "REPLACE - Part needs to be replaced"),
                ("YELLOW", "#FFCC00", "REPAIR - Part can be repaired"),
                ("BLUE", "#4488FF", "R&I - Remove and Install (for access)"),
                ("GREEN", "#44CC44", "BLEND - Adjacent panel needs blending"),
                ("ORANGE", "#FF8800", "SUPPLEMENT - Additional damage found")
            };

            foreach (var (color, hex, meaning) in colors)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Color swatch
                var swatch = new Border
                {
                    Width = 60,
                    Height = 24,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(ParseColor(hex)),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                swatch.Child = new TextBlock
                {
                    Text = color,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(swatch, 0);
                row.Children.Add(swatch);

                // Meaning
                var meaningText = new TextBlock
                {
                    Text = meaning,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(meaningText, 1);
                row.Children.Add(meaningText);

                panel.Children.Add(row);
            }

            panel.Children.Add(new TextBlock
            {
                Text = "Tip: Consistent color coding across your shop makes photos instantly understandable to anyone reviewing them.",
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            return new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Child = panel
            };
        }

        private Windows.UI.Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }

        private class TipItem
        {
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public string Icon { get; set; } = "\uE946"; // Default info icon
        }

        private void QueryButton_Click(object sender, RoutedEventArgs e)
        {
            var query = _queryInput.Text;
            if (string.IsNullOrWhiteSpace(query))
                return;

            ExecuteQuery(query);
        }

        private void QuickQuery_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string part)
            {
                _queryInput.Text = $"What operations do I need for a {part.ToLower()}?";
                ExecuteQuery($"repairing a {part.ToLower()}");
            }
        }

        private void ExecuteQuery(string query)
        {
            var learningService = EstimateLearningService.Instance;
            var result = learningService.QueryFromNaturalLanguage(query);

            var resultText = new System.Text.StringBuilder();
            resultText.AppendLine(result.Explanation);
            resultText.AppendLine();

            if (result.SuggestedOperations.Any())
            {
                resultText.AppendLine("SUGGESTED OPERATIONS:");
                resultText.AppendLine(new string('-', 50));

                foreach (var op in result.SuggestedOperations)
                {
                    resultText.AppendLine($"  {op.OperationType}: {op.Description}");
                    resultText.AppendLine($"    - Category: {op.Category}");
                    resultText.AppendLine($"    - Typical Labor: {op.TypicalLaborHours:F1} hrs");
                    if (op.TypicalRefinishHours > 0)
                        resultText.AppendLine($"    - Typical Refinish: {op.TypicalRefinishHours:F1} hrs");
                    if (op.TypicalPrice > 0)
                        resultText.AppendLine($"    - Typical Price: ${op.TypicalPrice:F2}");
                    resultText.AppendLine($"    - Confidence: {op.Confidence:P0}");
                    resultText.AppendLine($"    - Based on: {op.ExampleCount} examples");
                    resultText.AppendLine();
                }

                if (result.RelatedParts.Any())
                {
                    resultText.AppendLine();
                    resultText.AppendLine($"RELATED PARTS (often done together):");
                    resultText.AppendLine($"  {string.Join(", ", result.RelatedParts)}");
                }
            }
            else
            {
                resultText.AppendLine("No learned operations found. Import more estimates to build the database.");
            }

            _queryResult.Text = resultText.ToString();
            _queryResult.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220));
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".pdf");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // Get the window handle for WinUI 3
            var hwnd = WindowNative.GetWindowHandle(McstudDesktop.App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            _importProgress.IsActive = true;
            _importStatusText.Text = $"Parsing {file.Name}...";
            _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 100));
            _parsedItemsListView.Items.Clear();

            try
            {
                // Parse the PDF
                var parser = EstimatePdfParser.Instance;
                var estimate = parser.ParsePdf(file.Path);

                // Display parsed items
                foreach (var item in estimate.LineItems)
                {
                    var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.OperationType,
                        Width = 80,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.PartName,
                        Width = 150,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = $"{item.LaborHours:F1} hrs",
                        Width = 60,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.Price > 0 ? $"${item.Price:F2}" : "",
                        Width = 80,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100))
                    });

                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = item.Description.Length > 50 ? item.Description.Substring(0, 50) + "..." : item.Description,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))
                    });

                    _parsedItemsListView.Items.Add(itemPanel);
                }

                // Convert to training data and learn
                var trainingData = parser.ConvertToTrainingData(estimate);
                var learningService = EstimateLearningService.Instance;
                learningService.LearnFromEstimate(trainingData);

                // Auto-save to Estimate History Database for DNA/payment tracking
                try
                {
                    var historyDb = EstimateHistoryDatabase.Instance;
                    var estimateId = historyDb.AddEstimate(estimate);
                    System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Auto-saved estimate {estimateId} from LearningCenter");
                }
                catch (Exception historyEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[EstimateHistory] Failed to auto-save: {historyEx.Message}");
                }

                // Update status
                _importStatusText.Text = $"Successfully imported {estimate.LineItems.Count} line items from {file.Name}\n" +
                                          $"Source: {estimate.Source}\n" +
                                          $"Vehicle: {estimate.VehicleInfo}\n" +
                                          $"VIN: {estimate.VIN}";
                _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100));

                // Refresh stats
                RefreshStats();
            }
            catch (Exception ex)
            {
                _importStatusText.Text = $"Error importing PDF: {ex.Message}";
                _importStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
            }
            finally
            {
                _importProgress.IsActive = false;
            }
        }

        private async void ClearData_Click(object sender, RoutedEventArgs e)
        {
            // Only allow clearing personal data — never shop/baseline data
            if (LearningModeService.Instance.CurrentMode != LearningMode.Personal)
            {
                var blockedDialog = new ContentDialog
                {
                    Title = "Cannot Clear Shop Data",
                    Content = "Shop baseline data is read-only and cannot be cleared.\nSwitch to Personal mode to manage your own learned data.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                await blockedDialog.ShowAsync();
                return;
            }

            // First confirmation
            var confirmDialog = new ContentDialog
            {
                Title = "Clear Your Personal Learned Data?",
                Content = "This will delete all YOUR learned patterns and training examples.\nShop baseline data will not be affected.\n\nThis cannot be undone.",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var firstResult = await confirmDialog.ShowAsync();
            if (firstResult != ContentDialogResult.Primary)
                return;

            // Second confirmation — type CONFIRM
            var confirmInput = new TextBox
            {
                PlaceholderText = "Type CONFIRM to proceed",
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Margin = new Thickness(0, 10, 0, 0)
            };

            var finalDialog = new ContentDialog
            {
                Title = "Type CONFIRM to Clear Data",
                Content = confirmInput,
                PrimaryButtonText = "Clear All Personal Data",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var finalResult = await finalDialog.ShowAsync();
            if (finalResult == ContentDialogResult.Primary && confirmInput.Text.Trim().Equals("CONFIRM", StringComparison.OrdinalIgnoreCase))
            {
                EstimateLearningService.Instance.ClearAllData();
                RefreshStats();
                _importStatusText.Text = "Personal learned data has been cleared.";
            }
        }

        private void RefreshStats()
        {
            var learningService = EstimateLearningService.Instance;
            var stats = learningService.GetStatistics();

            // Get mining engine stats
            var miningStats = EstimateMiningEngine.Instance.GetStatistics();
            var partStats = PartRecognitionEngine.Instance.GetStats();

            var statsBuilder = new System.Text.StringBuilder();
            statsBuilder.AppendLine($"LEARNING DATABASE STATISTICS");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Total Patterns Learned:     {stats.TotalPatterns}");
            statsBuilder.AppendLine($"Total Training Examples:    {stats.TotalExamples}");
            statsBuilder.AppendLine($"Estimates Processed:        {stats.TotalEstimatesTrained}");
            statsBuilder.AppendLine($"Average Pattern Confidence: {stats.AverageConfidence:P0}");
            statsBuilder.AppendLine();

            // Mining Engine Stats
            statsBuilder.AppendLine($"PATTERN MINING ENGINE");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Estimates Mined:            {miningStats.TotalEstimatesMined}");
            statsBuilder.AppendLine($"Lines Analyzed:             {miningStats.TotalLinesMined:N0}");
            statsBuilder.AppendLine($"Parts Learned:              {miningStats.TotalPartsLearned}");
            statsBuilder.AppendLine($"Part Aliases:               {miningStats.TotalAliasesLearned}");
            statsBuilder.AppendLine($"Co-occurrence Patterns:     {miningStats.TotalCoOccurrencePatterns}");
            statsBuilder.AppendLine($"Formulas Discovered:        {miningStats.TotalFormulasLearned}");
            statsBuilder.AppendLine($"Total Value Analyzed:       {miningStats.TotalValueMined:C0}");
            if (miningStats.LastMiningRun > DateTime.MinValue)
            {
                statsBuilder.AppendLine($"Last Updated:               {miningStats.LastMiningRun:g}");
            }
            statsBuilder.AppendLine();

            // Part Recognition Stats
            statsBuilder.AppendLine($"PART RECOGNITION ENGINE");
            statsBuilder.AppendLine(new string('=', 40));
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"Canonical Parts:            {partStats.TotalCanonicalParts}");
            statsBuilder.AppendLine($"Learned Aliases:            {partStats.TotalAliasesLearned}");
            statsBuilder.AppendLine($"Bootstrap Parts:            {partStats.BootstrapPartsCount}");
            statsBuilder.AppendLine($"Abbreviations:              {partStats.AbbreviationsCount}");
            statsBuilder.AppendLine();

            // Top mined parts
            if (miningStats.TopParts.Any())
            {
                statsBuilder.AppendLine($"TOP LEARNED PARTS:");
                statsBuilder.AppendLine(new string('-', 40));
                foreach (var part in miningStats.TopParts.Take(10))
                {
                    statsBuilder.AppendLine($"  {part.Name,-30} ({part.TimesSeen}x, {part.AliasCount} aliases)");
                }
                statsBuilder.AppendLine();
            }

            if (stats.TopPatterns.Any())
            {
                statsBuilder.AppendLine($"TOP PATTERNS:");
                statsBuilder.AppendLine(new string('-', 40));
                foreach (var pattern in stats.TopPatterns)
                {
                    statsBuilder.AppendLine($"  - {pattern}");
                }
            }
            else
            {
                statsBuilder.AppendLine("No patterns learned yet. Import some estimates to get started!");
            }

            _statsText.Text = statsBuilder.ToString();

            // Refresh patterns list
            RefreshPatternsList();
        }

        private void RefreshPatternsList()
        {
            _patternsListView.Items.Clear();

            var patterns = EstimateLearningService.Instance.GetAllPatterns()
                .OrderByDescending(p => p.Confidence)
                .ThenByDescending(p => p.ExampleCount);

            foreach (var pattern in patterns)
            {
                var patternPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

                var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"{pattern.PartName} - {pattern.OperationType}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                });

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"Confidence: {pattern.Confidence:P0}",
                    Foreground = pattern.Confidence > 0.7
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 100))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 100))
                });

                headerStack.Children.Add(new TextBlock
                {
                    Text = $"({pattern.ExampleCount} examples)",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150))
                });

                patternPanel.Children.Add(headerStack);

                // Show operations for this pattern
                foreach (var op in pattern.Operations.Take(3))
                {
                    patternPanel.Children.Add(new TextBlock
                    {
                        Text = $"    -> {op.Description} ({op.LaborHours:F1} hrs)",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                        FontSize = 12
                    });
                }

                if (pattern.Operations.Count > 3)
                {
                    patternPanel.Children.Add(new TextBlock
                    {
                        Text = $"    ... and {pattern.Operations.Count - 3} more operations",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120)),
                        FontSize = 12,
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    });
                }

                _patternsListView.Items.Add(patternPanel);
            }

            if (!patterns.Any())
            {
                _patternsListView.Items.Add(new TextBlock
                {
                    Text = "No patterns learned yet. Import estimates to build the database.",
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }
        }
    }
}
