using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using McStudDesktop.Services;

// Alias for FlaUI Window to avoid conflict with WinUI Window
using FlaWindow = FlaUI.Core.AutomationElements.Window;

namespace McStudDesktop.Views;

/// <summary>
/// UI Inspector view for discovering controls in external applications
/// Used to map CCC Desktop, Mitchell, etc. for automation
/// </summary>
public class UIInspectorView : UserControl
{
    private UIAutomationService? _automationService;
    private Microsoft.UI.Xaml.Controls.ComboBox _windowSelector = null!;
    private TextBlock _statusText = null!;
    private Microsoft.UI.Xaml.Controls.TextBox _inspectorOutput = null!;
    private Microsoft.UI.Xaml.Controls.Button _refreshButton = null!;
    private Microsoft.UI.Xaml.Controls.Button _inspectButton = null!;
    private Microsoft.UI.Xaml.Controls.Button _findFieldsButton = null!;
    private Microsoft.UI.Xaml.Controls.Button _findButtonsButton = null!;
    private List<WindowInfo> _windows = new();
    private FlaWindow? _selectedWindow;

    public UIInspectorView()
    {
        BuildUI();
        InitializeService();
    }

    private void BuildUI()
    {
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Padding = new Thickness(20),
            RowSpacing = 15
        };

        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var titleText = new TextBlock
        {
            Text = "UI Automation Inspector",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        Grid.SetRow(titleText, 0);
        rootGrid.Children.Add(titleText);

        // Description
        var descText = new TextBlock
        {
            Text = "Use this tool to discover UI controls in CCC Desktop, Mitchell, and other applications.\nSelect a window below and click 'Inspect' to see its control structure.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(descText, 1);
        rootGrid.Children.Add(descText);

        // Window selector row
        var selectorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        Grid.SetRow(selectorPanel, 2);

        _windowSelector = new Microsoft.UI.Xaml.Controls.ComboBox
        {
            Width = 400,
            PlaceholderText = "Select a window to inspect..."
        };
        _windowSelector.SelectionChanged += WindowSelector_SelectionChanged;
        selectorPanel.Children.Add(_windowSelector);

        _refreshButton = CreateButton("🔄 Refresh", RefreshWindows_Click);
        selectorPanel.Children.Add(_refreshButton);

        _inspectButton = CreateButton("🔍 Inspect Tree", InspectWindow_Click);
        _inspectButton.IsEnabled = false;
        selectorPanel.Children.Add(_inspectButton);

        _findFieldsButton = CreateButton("📝 Find Text Fields", FindFields_Click);
        _findFieldsButton.IsEnabled = false;
        selectorPanel.Children.Add(_findFieldsButton);

        _findButtonsButton = CreateButton("🔘 Find Buttons", FindButtons_Click);
        _findButtonsButton.IsEnabled = false;
        selectorPanel.Children.Add(_findButtonsButton);

        // Quick CCC Inspect button - always enabled
        var quickCCCButton = CreateButton("⚡ Quick CCC Inspect", QuickCCCInspect_Click);
        quickCCCButton.Background = new SolidColorBrush(Color.FromArgb(255, 0, 100, 180));
        selectorPanel.Children.Add(quickCCCButton);

        rootGrid.Children.Add(selectorPanel);

        // Output area
        var outputBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10)
        };
        Grid.SetRow(outputBorder, 3);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _inspectorOutput = new Microsoft.UI.Xaml.Controls.TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
            BorderThickness = new Thickness(0),
            Text = "Click 'Refresh' to see available windows, then select one and click 'Inspect' to view its UI structure.\n\n" +
                   "Tips:\n" +
                   "• Look for 'AutomationId' values - these are the most reliable for automation\n" +
                   "• 'Name' is the visible text on controls\n" +
                   "• [Edit] = text fields, [Button] = buttons, [ComboBox] = dropdowns\n" +
                   "• [DataGrid] or [Table] = data grids where estimate lines might be"
        };

        scrollViewer.Content = _inspectorOutput;
        outputBorder.Child = scrollViewer;
        rootGrid.Children.Add(outputBorder);

        // Status bar
        _statusText = new TextBlock
        {
            Text = "Ready - Click 'Refresh' to start",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
        };
        Grid.SetRow(_statusText, 4);
        rootGrid.Children.Add(_statusText);

        Content = rootGrid;
    }

    private Microsoft.UI.Xaml.Controls.Button CreateButton(string text, RoutedEventHandler handler)
    {
        var button = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = text,
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80))
        };
        button.Click += handler;
        return button;
    }

    private void InitializeService()
    {
        try
        {
            _automationService = new UIAutomationService();
            _statusText.Text = "UI Automation service initialized";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error initializing: {ex.Message}";
            _inspectorOutput.Text = $"Failed to initialize UI Automation:\n{ex}";
        }
    }

    private async void RefreshWindows_Click(object sender, RoutedEventArgs e)
    {
        if (_automationService == null) return;

        _statusText.Text = "Scanning for windows...";
        _refreshButton.IsEnabled = false;

        try
        {
            await Task.Run(() =>
            {
                _windows = _automationService.GetAllWindows();
            });

            _windowSelector.Items.Clear();
            foreach (var window in _windows)
            {
                _windowSelector.Items.Add($"{window.ProcessName}: {window.Title}");
            }

            _statusText.Text = $"Found {_windows.Count} windows";

            // Highlight if we find CCC or Mitchell
            var cccWindow = _windows.Find(w =>
                w.ProcessName.Contains("CCC", StringComparison.OrdinalIgnoreCase) ||
                w.Title.Contains("CCC", StringComparison.OrdinalIgnoreCase));

            var mitchellWindow = _windows.Find(w =>
                w.ProcessName.Contains("Mitchell", StringComparison.OrdinalIgnoreCase) ||
                w.Title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase));

            if (cccWindow != null || mitchellWindow != null)
            {
                var found = new List<string>();
                if (cccWindow != null) found.Add("CCC");
                if (mitchellWindow != null) found.Add("Mitchell");
                _statusText.Text = $"Found {_windows.Count} windows - Detected: {string.Join(", ", found)}!";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void WindowSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = _windowSelector.SelectedIndex;
        if (index >= 0 && index < _windows.Count)
        {
            var windowInfo = _windows[index];

            // Find the actual window
            _selectedWindow = _automationService?.FindWindowByTitle(windowInfo.Title);

            var hasWindow = _selectedWindow != null;
            _inspectButton.IsEnabled = hasWindow;
            _findFieldsButton.IsEnabled = hasWindow;
            _findButtonsButton.IsEnabled = hasWindow;

            if (hasWindow)
            {
                _statusText.Text = $"Selected: {windowInfo.ProcessName} - {windowInfo.Title}";
            }
            else
            {
                _statusText.Text = "Could not attach to window (it may have closed)";
            }
        }
    }

    private async void InspectWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_automationService == null || _selectedWindow == null) return;

        _statusText.Text = "Inspecting window structure (this may take a moment)...";
        _inspectButton.IsEnabled = false;

        try
        {
            string result = "";
            await Task.Run(() =>
            {
                result = _automationService.InspectWindow(_selectedWindow, maxDepth: 5);
            });

            _inspectorOutput.Text = result;
            _statusText.Text = "Inspection complete - scroll through the output to find controls";
        }
        catch (Exception ex)
        {
            _inspectorOutput.Text = $"Error inspecting window:\n{ex}";
            _statusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _inspectButton.IsEnabled = true;
        }
    }

    private async void FindFields_Click(object sender, RoutedEventArgs e)
    {
        if (_automationService == null || _selectedWindow == null) return;

        _statusText.Text = "Finding text fields...";
        _findFieldsButton.IsEnabled = false;

        try
        {
            List<ControlInfo> fields = new();
            await Task.Run(() =>
            {
                fields = _automationService.FindAllTextFields(_selectedWindow);
            });

            var output = $"=== Found {fields.Count} Text Fields ===\n\n";
            foreach (var field in fields)
            {
                output += $"Name: \"{field.Name}\"\n";
                output += $"  AutomationId: \"{field.AutomationId}\"\n";
                output += $"  ClassName: \"{field.ClassName}\"\n";
                output += $"  Current Value: \"{field.CurrentValue}\"\n\n";
            }

            _inspectorOutput.Text = output;
            _statusText.Text = $"Found {fields.Count} text fields";
        }
        catch (Exception ex)
        {
            _inspectorOutput.Text = $"Error finding fields:\n{ex}";
            _statusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _findFieldsButton.IsEnabled = true;
        }
    }

    private async void FindButtons_Click(object sender, RoutedEventArgs e)
    {
        if (_automationService == null || _selectedWindow == null) return;

        _statusText.Text = "Finding buttons...";
        _findButtonsButton.IsEnabled = false;

        try
        {
            List<ControlInfo> buttons = new();
            await Task.Run(() =>
            {
                buttons = _automationService.FindAllButtons(_selectedWindow);
            });

            var output = $"=== Found {buttons.Count} Buttons ===\n\n";
            foreach (var button in buttons)
            {
                output += $"Name: \"{button.Name}\"\n";
                output += $"  AutomationId: \"{button.AutomationId}\"\n";
                output += $"  ClassName: \"{button.ClassName}\"\n\n";
            }

            _inspectorOutput.Text = output;
            _statusText.Text = $"Found {buttons.Count} buttons";
        }
        catch (Exception ex)
        {
            _inspectorOutput.Text = $"Error finding buttons:\n{ex}";
            _statusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _findButtonsButton.IsEnabled = true;
        }
    }

    private async void QuickCCCInspect_Click(object sender, RoutedEventArgs e)
    {
        if (_automationService == null) return;

        _statusText.Text = "Inspecting CCC Desktop...";

        try
        {
            string result = "";
            await Task.Run(() =>
            {
                result = _automationService.InspectCCCWindow();
            });

            _inspectorOutput.Text = result;
            _statusText.Text = "CCC inspection complete - look for field names and AutomationIds";
        }
        catch (Exception ex)
        {
            _inspectorOutput.Text = $"Error inspecting CCC Desktop:\n{ex}";
            _statusText.Text = $"Error: {ex.Message}";
        }
    }
}
