using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Windows.UI;
using System;
using System.Collections.Generic;

namespace McStudDesktop.Views;

/// <summary>
/// Simplified SOP List that actually works - matches Excel exactly
/// </summary>
public class SOPListView_Simple : Grid
{
    private string _currentSection = "Electrical";

    // Pre-created section grids
    private Dictionary<string, Grid> _sectionGrids = new Dictionary<string, Grid>();

    // Input controls
    private ComboBox? _batteryTypeCombo;
    private ComboBox? _adasCombo;
    private ComboBox? _testBatteryCombo;

    // Output TextBlocks
    private Dictionary<string, TextBlock> _outputs = new Dictionary<string, TextBlock>();

    public SOPListView_Simple()
    {
        Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
        CreateLayout();
    }

    private void CreateLayout()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); // Sidebar
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

        // Sidebar
        var sidebar = CreateSidebar();
        Grid.SetColumn(sidebar, 0);
        Children.Add(sidebar);

        // Pre-create all section grids
        var sections = new[] { "Electrical", "Vehicle Diagnostics", "Misc" };
        foreach (var section in sections)
        {
            var grid = CreateContentForSection(section);
            Grid.SetColumn(grid, 1);
            grid.Visibility = (section == _currentSection) ? Visibility.Visible : Visibility.Collapsed;
            _sectionGrids[section] = grid;
            Children.Add(grid);
        }
    }

    private StackPanel CreateSidebar()
    {
        var stack = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 25, 25)),
            Padding = new Thickness(10)
        };

        stack.Children.Add(CreateSectionButton("Electrical"));
        stack.Children.Add(CreateSectionButton("Vehicle Diagnostics"));
        stack.Children.Add(CreateSectionButton("Misc"));

        return stack;
    }

    private Button CreateSectionButton(string sectionName)
    {
        var btn = new Button
        {
            Content = sectionName,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 5, 0, 5),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Foreground = new SolidColorBrush(Colors.White)
        };

        btn.Click += (s, e) => SwitchSection(sectionName);

        return btn;
    }

    private void SwitchSection(string sectionName)
    {
        // Hide current section
        if (_sectionGrids.ContainsKey(_currentSection))
        {
            _sectionGrids[_currentSection].Visibility = Visibility.Collapsed;
        }

        // Show new section
        _currentSection = sectionName;
        if (_sectionGrids.ContainsKey(sectionName))
        {
            _sectionGrids[sectionName].Visibility = Visibility.Visible;
        }
    }

    private Grid CreateContentForSection(string sectionName)
    {
        var grid = new Grid
        {
            Padding = new Thickness(20)
        };

        // Create columns: Inputs (A, B, C, D) ... Outputs (M, O, Q, R, V, W, X)
        for (int i = 0; i < 24; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        }

        // Create rows
        for (int i = 0; i < 30; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        }

        int row = 0;

        // Header
        AddText(grid, row, 0, $"{sectionName} Section", true);
        row++;

        // Column headers
        AddText(grid, row, 0, "Input", true);
        AddText(grid, row, 12, "Operation", true);
        AddText(grid, row, 14, "Description", true);
        AddText(grid, row, 16, "Qty", true);
        AddText(grid, row, 17, "Price", true);
        AddText(grid, row, 21, "Labor", true);
        AddText(grid, row, 22, "Cat", true);
        row++;

        // Add section-specific content
        if (sectionName == "Electrical")
        {
            AddElectricalRows(grid, ref row);
        }
        else if (sectionName == "Vehicle Diagnostics")
        {
            AddVehicleDiagnosticsRows(grid, ref row);
        }
        else if (sectionName == "Misc")
        {
            AddMiscRows(grid, ref row);
        }

        return grid;
    }

    private void AddElectricalRows(Grid grid, ref int row)
    {
        // Row 1: Battery Type Input
        AddText(grid, row, 0, "Battery Type:");
        _batteryTypeCombo = AddComboBox(grid, row, 1, new[] { "", "Single", "Dual" });
        _batteryTypeCombo.SelectionChanged += BatteryType_Changed;

        // Outputs for Battery Disconnect
        AddOutputText(grid, row, "battery_op", 12);
        AddOutputText(grid, row, "battery_desc", 14);
        AddOutputText(grid, row, "battery_qty", 16);
        AddOutputText(grid, row, "battery_labor", 21);
        row++;

        // Row 2: Test Battery Input
        AddText(grid, row, 0, "Test Battery:");
        _testBatteryCombo = AddComboBox(grid, row, 1, new[] { "", "Yes", "No" });
        _testBatteryCombo.SelectionChanged += TestBattery_Changed;

        // Outputs for Test Battery
        AddOutputText(grid, row, "test_op", 12);
        AddOutputText(grid, row, "test_desc", 14);
        AddOutputText(grid, row, "test_qty", 16);
        AddOutputText(grid, row, "test_labor", 21);
        row++;

        // Row 3: ADAS Input
        AddText(grid, row, 0, "ADAS:");
        _adasCombo = AddComboBox(grid, row, 1, new[] { "", "Yes", "No" });
        _adasCombo.SelectionChanged += (s, e) => UpdateOutputs();
        row++;
    }

    private void AddVehicleDiagnosticsRows(Grid grid, ref int row)
    {
        AddText(grid, row, 0, "Vehicle Diagnostics operations coming soon...");
        row++;
    }

    private void AddMiscRows(Grid grid, ref int row)
    {
        AddText(grid, row, 0, "Misc operations coming soon...");
        row++;
    }

    private void BatteryType_Changed(object sender, SelectionChangedEventArgs e)
    {
        var selected = GetComboValue(_batteryTypeCombo);

        if (selected == "Single")
        {
            SetOutput("battery_op", "Rpr");
            SetOutput("battery_desc", "Disconnect and Reconnect Battery");
            SetOutput("battery_qty", "1");
            SetOutput("battery_labor", "0.4");
        }
        else if (selected == "Dual")
        {
            SetOutput("battery_op", "Rpr");
            SetOutput("battery_desc", "Disconnect and Reconnect 2x Battery");
            SetOutput("battery_qty", "1");
            SetOutput("battery_labor", "0.8");
        }
        else
        {
            SetOutput("battery_op", "");
            SetOutput("battery_desc", "");
            SetOutput("battery_qty", "");
            SetOutput("battery_labor", "");
        }
    }

    private void TestBattery_Changed(object sender, SelectionChangedEventArgs e)
    {
        var selected = GetComboValue(_testBatteryCombo);

        if (selected == "Yes")
        {
            SetOutput("test_op", "Rpr");
            SetOutput("test_desc", "Test Battery Condition");
            SetOutput("test_qty", "1");
            SetOutput("test_labor", "0.2");
        }
        else
        {
            SetOutput("test_op", "");
            SetOutput("test_desc", "");
            SetOutput("test_qty", "");
            SetOutput("test_labor", "");
        }
    }

    private void UpdateOutputs()
    {
        // Re-trigger all updates
        if (_batteryTypeCombo != null)
            BatteryType_Changed(_batteryTypeCombo, null!);
        if (_testBatteryCombo != null)
            TestBattery_Changed(_testBatteryCombo, null!);
    }

    private void AddText(Grid grid, int row, int col, string text, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5)
        };

        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private void AddOutputText(Grid grid, int row, string key, int col)
    {
        var tb = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Colors.LightGreen),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5)
        };

        _outputs[key] = tb;

        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private ComboBox AddComboBox(Grid grid, int row, int col, string[] items)
    {
        var combo = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
            Foreground = new SolidColorBrush(Colors.White),
            Width = 150
        };

        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        combo.SelectedIndex = 0;

        Grid.SetRow(combo, row);
        Grid.SetColumn(combo, col);
        grid.Children.Add(combo);

        return combo;
    }

    private string GetComboValue(ComboBox? combo)
    {
        if (combo == null || combo.SelectedItem == null) return "";
        return combo.SelectedItem.ToString() ?? "";
    }

    private void SetOutput(string key, string value)
    {
        if (_outputs.ContainsKey(key))
        {
            _outputs[key].Text = value;
        }
    }
}
