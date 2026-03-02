using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

// Aliases to avoid conflicts with WinUI types
using FlaWindow = FlaUI.Core.AutomationElements.Window;
using FlaButton = FlaUI.Core.AutomationElements.Button;
using FlaTextBox = FlaUI.Core.AutomationElements.TextBox;
using FlaComboBox = FlaUI.Core.AutomationElements.ComboBox;

namespace McStudDesktop.Services;

/// <summary>
/// Service for automating data entry into external estimating applications
/// using Windows UI Automation (no API calls, completely local)
/// </summary>
public class UIAutomationService : IDisposable
{
    private readonly UIA3Automation _automation;
    private bool _disposed;

    public UIAutomationService()
    {
        _automation = new UIA3Automation();
    }

    /// <summary>
    /// Gets a list of all visible windows with their process names
    /// Useful for finding CCC Desktop, Mitchell, etc.
    /// </summary>
    public List<WindowInfo> GetAllWindows()
    {
        var windows = new List<WindowInfo>();
        var desktop = _automation.GetDesktop();
        var allWindows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        foreach (var window in allWindows)
        {
            try
            {
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = Process.GetProcessById(processId);

                windows.Add(new WindowInfo
                {
                    Title = window.Name ?? "(No Title)",
                    ProcessName = process.ProcessName,
                    ProcessId = processId,
                    AutomationId = window.Properties.AutomationId.ValueOrDefault ?? "",
                    ClassName = window.Properties.ClassName.ValueOrDefault ?? ""
                });
            }
            catch
            {
                // Process may have exited, skip
            }
        }

        return windows.OrderBy(w => w.ProcessName).ToList();
    }

    /// <summary>
    /// Finds a window by partial title match
    /// </summary>
    public FlaWindow? FindWindowByTitle(string partialTitle)
    {
        var desktop = _automation.GetDesktop();
        var allWindows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        foreach (var window in allWindows)
        {
            var title = window.Name ?? "";
            if (title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                return window.AsWindow();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a window by process name
    /// </summary>
    public FlaWindow? FindWindowByProcessName(string processName)
    {
        var desktop = _automation.GetDesktop();
        var allWindows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        foreach (var window in allWindows)
        {
            try
            {
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = Process.GetProcessById(processId);

                if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    return window.AsWindow();
                }
            }
            catch
            {
                // Process may have exited
            }
        }

        return null;
    }

    /// <summary>
    /// Inspects a window and returns its UI tree structure
    /// This is key for mapping CCC Desktop's controls
    /// </summary>
    public string InspectWindow(FlaWindow window, int maxDepth = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Window: {window.Name} ===");
        sb.AppendLine($"AutomationId: {window.Properties.AutomationId.ValueOrDefault}");
        sb.AppendLine($"ClassName: {window.Properties.ClassName.ValueOrDefault}");
        sb.AppendLine();

        InspectElement(window, sb, 0, maxDepth);

        return sb.ToString();
    }

    private void InspectElement(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);
        var controlType = element.Properties.ControlType.ValueOrDefault;
        var name = element.Properties.Name.ValueOrDefault ?? "";
        var automationId = element.Properties.AutomationId.ValueOrDefault ?? "";
        var className = element.Properties.ClassName.ValueOrDefault ?? "";

        // Only show elements with meaningful info
        if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(automationId))
        {
            sb.AppendLine($"{indent}[{controlType}] Name=\"{Truncate(name, 50)}\" AutomationId=\"{automationId}\" Class=\"{className}\"");
        }
        else if (controlType == ControlType.Edit || controlType == ControlType.Button ||
                 controlType == ControlType.ComboBox || controlType == ControlType.DataGrid ||
                 controlType == ControlType.List || controlType == ControlType.Table)
        {
            // Always show interactive controls even without names
            sb.AppendLine($"{indent}[{controlType}] AutomationId=\"{automationId}\" Class=\"{className}\"");
        }

        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                InspectElement(child, sb, depth + 1, maxDepth);
            }
        }
        catch
        {
            // Some elements don't allow child enumeration
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    /// <summary>
    /// Finds all text fields (Edit controls) in a window
    /// </summary>
    public List<ControlInfo> FindAllTextFields(FlaWindow window)
    {
        var controls = new List<ControlInfo>();
        var edits = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));

        foreach (var edit in edits)
        {
            controls.Add(new ControlInfo
            {
                ControlType = "Edit",
                Name = edit.Properties.Name.ValueOrDefault ?? "",
                AutomationId = edit.Properties.AutomationId.ValueOrDefault ?? "",
                ClassName = edit.Properties.ClassName.ValueOrDefault ?? "",
                CurrentValue = edit.AsTextBox()?.Text ?? ""
            });
        }

        return controls;
    }

    /// <summary>
    /// Finds all buttons in a window
    /// </summary>
    public List<ControlInfo> FindAllButtons(FlaWindow window)
    {
        var controls = new List<ControlInfo>();
        var buttons = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));

        foreach (var button in buttons)
        {
            controls.Add(new ControlInfo
            {
                ControlType = "Button",
                Name = button.Properties.Name.ValueOrDefault ?? "",
                AutomationId = button.Properties.AutomationId.ValueOrDefault ?? "",
                ClassName = button.Properties.ClassName.ValueOrDefault ?? ""
            });
        }

        return controls;
    }

    /// <summary>
    /// Sets text in a text field by AutomationId
    /// </summary>
    public bool SetTextByAutomationId(FlaWindow window, string automationId, string value)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element == null) return false;

            var textBox = element.AsTextBox();
            if (textBox == null) return false;

            textBox.Text = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets text in a text field by Name
    /// </summary>
    public bool SetTextByName(FlaWindow window, string name, string value)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name));
            if (element == null) return false;

            var textBox = element.AsTextBox();
            if (textBox == null) return false;

            textBox.Text = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clicks a button by AutomationId
    /// </summary>
    public bool ClickButtonByAutomationId(FlaWindow window, string automationId)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element == null) return false;

            var button = element.AsButton();
            if (button == null) return false;

            button.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clicks a button by Name
    /// </summary>
    public bool ClickButtonByName(FlaWindow window, string name)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name));
            if (element == null) return false;

            var button = element.AsButton();
            if (button == null) return false;

            button.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    #region Human-Like Input Methods

    // Win32 API for keyboard input
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;

    private readonly Random _random = new();

    /// <summary>
    /// Human-like typing delay range (milliseconds)
    /// </summary>
    public int MinTypeDelay { get; set; } = 15;
    public int MaxTypeDelay { get; set; } = 45;

    /// <summary>
    /// Delay after clicking before typing (milliseconds)
    /// </summary>
    public int ClickToTypeDelay { get; set; } = 50;

    /// <summary>
    /// Finds an element by AutomationId, clicks it, and types text with human-like delays
    /// </summary>
    public async Task<bool> ClickAndTypeByAutomationIdAsync(FlaWindow window, string automationId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element == null) return false;

            return await ClickAndTypeAsync(element, text, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIA] ClickAndType error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds an element by Name, clicks it, and types text with human-like delays
    /// </summary>
    public async Task<bool> ClickAndTypeByNameAsync(FlaWindow window, string name, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name));
            if (element == null) return false;

            return await ClickAndTypeAsync(element, text, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIA] ClickAndType error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds an element by ClassName, clicks it, and types text with human-like delays
    /// </summary>
    public async Task<bool> ClickAndTypeByClassNameAsync(FlaWindow window, string className, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var element = window.FindFirstDescendant(cf => cf.ByClassName(className));
            if (element == null) return false;

            return await ClickAndTypeAsync(element, text, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIA] ClickAndType error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clicks an element and types text with human-like delays
    /// </summary>
    public async Task<bool> ClickAndTypeAsync(AutomationElement element, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get element's clickable point
            var rect = element.BoundingRectangle;
            var centerX = (int)(rect.X + rect.Width / 2);
            var centerY = (int)(rect.Y + rect.Height / 2);

            // Click using FlaUI's Mouse helper (simulates real mouse click)
            Mouse.Click(new System.Drawing.Point(centerX, centerY));

            // Wait for focus
            await Task.Delay(ClickToTypeDelay, cancellationToken);

            // Clear existing text (select all + delete)
            SendKey(VK_CONTROL, true);
            SendKeyPress((byte)'A');
            SendKey(VK_CONTROL, false);
            await Task.Delay(20, cancellationToken);

            // Type with human-like delays
            await TypeTextHumanLikeAsync(text, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIA] ClickAndType error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Types text with human-like random delays between keystrokes
    /// </summary>
    public async Task TypeTextHumanLikeAsync(string text, CancellationToken cancellationToken = default)
    {
        foreach (char c in text)
        {
            if (cancellationToken.IsCancellationRequested) break;

            TypeCharacter(c);

            // Random delay between keystrokes for human-like typing
            int delay = _random.Next(MinTypeDelay, MaxTypeDelay);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// Types a single character using keyboard simulation
    /// </summary>
    private void TypeCharacter(char c)
    {
        short vkResult = VkKeyScan(c);
        byte vkCode = (byte)(vkResult & 0xFF);
        bool needShift = (vkResult & 0x100) != 0;

        if (needShift)
        {
            SendKey(VK_SHIFT, true);
        }

        SendKeyPress(vkCode);

        if (needShift)
        {
            SendKey(VK_SHIFT, false);
        }
    }

    /// <summary>
    /// Sends a key down or up event
    /// </summary>
    private void SendKey(byte vkCode, bool down)
    {
        uint flags = down ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;
        keybd_event(vkCode, 0, flags, UIntPtr.Zero);
    }

    /// <summary>
    /// Sends a key press (down + up)
    /// </summary>
    private void SendKeyPress(byte vkCode)
    {
        keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// Press Tab key
    /// </summary>
    public void PressTab()
    {
        SendKeyPress(VK_TAB);
    }

    /// <summary>
    /// Press Enter key
    /// </summary>
    public void PressEnter()
    {
        SendKeyPress(VK_RETURN);
    }

    /// <summary>
    /// Finds a DataGrid/Table row by row index in CCC Desktop
    /// </summary>
    public AutomationElement? FindDataGridRow(FlaWindow window, int rowIndex)
    {
        try
        {
            // Find the main data grid
            var dataGrid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
            if (dataGrid == null)
            {
                dataGrid = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Table));
            }
            if (dataGrid == null) return null;

            // Find all rows
            var rows = dataGrid.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem));
            if (rows == null || rowIndex >= rows.Length) return null;

            return rows[rowIndex];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds cells in a data row
    /// </summary>
    public AutomationElement[] FindRowCells(AutomationElement row)
    {
        try
        {
            var cells = row.FindAllChildren();
            return cells;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    /// <summary>
    /// Clicks on a specific cell in a data row and types text
    /// </summary>
    public async Task<bool> ClickCellAndTypeAsync(AutomationElement row, int cellIndex, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var cells = FindRowCells(row);
            if (cellIndex >= cells.Length) return false;

            var cell = cells[cellIndex];
            return await ClickAndTypeAsync(cell, text, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get detailed inspection of CCC-specific controls
    /// </summary>
    public string InspectCCCWindow()
    {
        var cccWindow = FindWindowByTitle("CCC");
        if (cccWindow == null)
        {
            return "CCC window not found. Make sure CCC Desktop is running.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== CCC Desktop UI Structure ===");
        sb.AppendLine();

        // Deep inspection
        sb.AppendLine("--- All Edit Fields ---");
        var edits = FindAllTextFields(cccWindow);
        foreach (var edit in edits)
        {
            sb.AppendLine($"  Name=\"{edit.Name}\" AutomationId=\"{edit.AutomationId}\" Class=\"{edit.ClassName}\" Value=\"{Truncate(edit.CurrentValue, 30)}\"");
        }

        sb.AppendLine();
        sb.AppendLine("--- All Buttons ---");
        var buttons = FindAllButtons(cccWindow);
        foreach (var btn in buttons)
        {
            sb.AppendLine($"  Name=\"{btn.Name}\" AutomationId=\"{btn.AutomationId}\" Class=\"{btn.ClassName}\"");
        }

        sb.AppendLine();
        sb.AppendLine("--- Data Grids/Tables ---");
        var grids = cccWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.DataGrid));
        foreach (var grid in grids)
        {
            sb.AppendLine($"  DataGrid: Name=\"{grid.Name}\" AutomationId=\"{grid.Properties.AutomationId.ValueOrDefault}\"");
        }
        var tables = cccWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Table));
        foreach (var table in tables)
        {
            sb.AppendLine($"  Table: Name=\"{table.Name}\" AutomationId=\"{table.Properties.AutomationId.ValueOrDefault}\"");
        }

        return sb.ToString();
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _automation?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Info about a discovered window
/// </summary>
public class WindowInfo
{
    public string Title { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public string AutomationId { get; set; } = "";
    public string ClassName { get; set; } = "";

    public override string ToString() => $"{ProcessName}: {Title}";
}

/// <summary>
/// Info about a discovered UI control
/// </summary>
public class ControlInfo
{
    public string ControlType { get; set; } = "";
    public string Name { get; set; } = "";
    public string AutomationId { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string CurrentValue { get; set; } = "";

    public override string ToString() => $"[{ControlType}] {Name} (ID: {AutomationId})";
}
