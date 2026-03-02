#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Windows.ApplicationModel.DataTransfer;

using FlaWindow = FlaUI.Core.AutomationElements.Window;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Intelligent automation service for CCC ONE
    /// Uses UI Automation to directly manipulate controls - no blind keystrokes!
    ///
    /// Advantages over AutoHotkey:
    /// - Directly targets CCC ONE's controls even if you click elsewhere
    /// - Faster because it sets values directly instead of simulating typing
    /// - More reliable - won't type into wrong window
    /// - Can verify data was entered correctly
    /// </summary>
    public class CCCAutomationService : IDisposable
    {
        private UIA3Automation? _automation;
        private FlaWindow? _cccWindow;
        private AutomationElement? _dataGrid;
        private bool _disposed;

        // Events for UI updates
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<CCCPasteProgressEventArgs>? ProgressChanged;
        public event EventHandler<CCCPasteCompletedEventArgs>? PasteCompleted;

        // CCC ONE window identifiers (may need adjustment based on actual CCC version)
        private static readonly string[] CCC_WINDOW_TITLES = new[]
        {
            "CCC ONE",
            "CCC Estimating",
            "CCCONE",
            "CCC Desktop",
            "Estimate"
        };

        private static readonly string[] CCC_PROCESS_NAMES = new[]
        {
            "cccone",
            "ccc",
            "cccdesktop",
            "CCCEstimate"
        };

        public bool IsConnected => _cccWindow != null;

        public CCCAutomationService()
        {
            _automation = new UIA3Automation();
        }

        /// <summary>
        /// Find and connect to CCC ONE window
        /// </summary>
        public bool ConnectToCCC()
        {
            if (_automation == null) return false;

            try
            {
                var desktop = _automation.GetDesktop();
                var allWindows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

                foreach (var window in allWindows)
                {
                    var title = window.Name ?? "";
                    var processId = window.Properties.ProcessId.ValueOrDefault;

                    // Check by title
                    foreach (var cccTitle in CCC_WINDOW_TITLES)
                    {
                        if (title.Contains(cccTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            _cccWindow = window.AsWindow();
                            StatusChanged?.Invoke(this, $"Connected to: {title}");
                            return true;
                        }
                    }

                    // Check by process name
                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById(processId);
                        foreach (var cccProcess in CCC_PROCESS_NAMES)
                        {
                            if (process.ProcessName.Contains(cccProcess, StringComparison.OrdinalIgnoreCase))
                            {
                                _cccWindow = window.AsWindow();
                                StatusChanged?.Invoke(this, $"Connected to: {process.ProcessName}");
                                return true;
                            }
                        }
                    }
                    catch { /* Process may have exited */ }
                }

                StatusChanged?.Invoke(this, "CCC ONE not found. Please open CCC ONE first.");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error connecting to CCC: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a list of ALL visible windows for user to select from
        /// User may have multiple CCC windows, home page, etc.
        /// </summary>
        public List<WindowCandidate> GetAllWindows()
        {
            var candidates = new List<WindowCandidate>();
            if (_automation == null) return candidates;

            try
            {
                var desktop = _automation.GetDesktop();
                var allWindows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

                foreach (var window in allWindows)
                {
                    var title = window.Name ?? "";
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (title.Length < 3) continue; // Skip tiny titles

                    var processId = window.Properties.ProcessId.ValueOrDefault;
                    string processName = "";

                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById(processId);
                        processName = process.ProcessName;
                    }
                    catch { continue; }

                    // Skip our own app and system windows
                    if (processName.Equals("McstudDesktop", StringComparison.OrdinalIgnoreCase)) continue;
                    if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && !title.Contains("Explorer")) continue;
                    if (processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)) continue;
                    if (processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)) continue;
                    if (processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)) continue;

                    // Check if it's likely an estimating app (for sorting priority)
                    bool isEstimatingApp = CCC_WINDOW_TITLES.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                                          CCC_PROCESS_NAMES.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
                                          title.Contains("Estimate", StringComparison.OrdinalIgnoreCase) ||
                                          title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase) ||
                                          title.Contains("Audatex", StringComparison.OrdinalIgnoreCase) ||
                                          title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                                          title.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                                          title.Contains("Firefox", StringComparison.OrdinalIgnoreCase);

                    candidates.Add(new WindowCandidate
                    {
                        Title = title,
                        ProcessName = processName,
                        ProcessId = processId,
                        Window = window.AsWindow(),
                        IsEstimatingApp = isEstimatingApp
                    });
                }
            }
            catch { }

            // Sort: Estimating apps first, then alphabetically
            return candidates
                .OrderByDescending(c => c.IsEstimatingApp)
                .ThenBy(c => c.Title)
                .ToList();
        }

        /// <summary>
        /// Get only windows that look like estimating software (CCC, Mitchell, browsers)
        /// </summary>
        public List<WindowCandidate> GetEstimatingWindows()
        {
            return GetAllWindows().Where(w => w.IsEstimatingApp).ToList();
        }

        /// <summary>
        /// Connect to a specific window by reference
        /// </summary>
        public void ConnectToWindow(FlaWindow window)
        {
            _cccWindow = window;
            StatusChanged?.Invoke(this, $"Connected to: {window.Name}");
        }

        /// <summary>
        /// Find the data entry grid/table in CCC ONE
        /// </summary>
        public bool FindDataGrid()
        {
            if (_cccWindow == null) return false;

            try
            {
                // Try to find a DataGrid or Table control
                var dataGrid = _cccWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid));
                if (dataGrid != null)
                {
                    _dataGrid = dataGrid;
                    StatusChanged?.Invoke(this, "Found data grid");
                    return true;
                }

                // Try Table control
                var table = _cccWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Table));
                if (table != null)
                {
                    _dataGrid = table;
                    StatusChanged?.Invoke(this, "Found data table");
                    return true;
                }

                // Try List control (sometimes grids appear as lists)
                var list = _cccWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.List));
                if (list != null)
                {
                    _dataGrid = list;
                    StatusChanged?.Invoke(this, "Found data list");
                    return true;
                }

                StatusChanged?.Invoke(this, "Could not find data entry area. Will use focused element.");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error finding data grid: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parse clipboard text into rows and columns
        /// </summary>
        public List<List<string>> ParseClipboardText(string text)
        {
            var rows = new List<List<string>>();

            if (string.IsNullOrWhiteSpace(text))
                return rows;

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                var fields = trimmedLine.Split('\t');
                var row = new List<string>();

                foreach (var field in fields)
                {
                    row.Add(field.Trim());
                }

                if (row.Count > 0)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>
        /// Get clipboard data
        /// </summary>
        public async Task<List<List<string>>> GetClipboardDataAsync()
        {
            try
            {
                var dataPackage = Clipboard.GetContent();
                if (dataPackage != null && dataPackage.Contains(StandardDataFormats.Text))
                {
                    var text = await dataPackage.GetTextAsync();
                    return ParseClipboardText(text);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
            }

            return new List<List<string>>();
        }

        /// <summary>
        /// Smart paste - pastes to the SELECTED target window (not auto-detected)
        /// User must select a window first using SelectTargetWindow or SetTargetWindow
        /// </summary>
        public async Task SmartPasteAsync(string? textToPaste = null, CancellationToken cancellationToken = default)
        {
            List<List<string>> rows;

            if (!string.IsNullOrEmpty(textToPaste))
            {
                rows = ParseClipboardText(textToPaste);
            }
            else
            {
                rows = await GetClipboardDataAsync();
            }

            if (rows.Count == 0)
            {
                StatusChanged?.Invoke(this, "No data to paste. Copy data from spreadsheet first.");
                return;
            }

            StatusChanged?.Invoke(this, $"Pasting {rows.Count} rows...");

            int totalRows = rows.Count;
            int processedRows = 0;
            bool success = true;

            try
            {
                // If user selected a specific target window, use it
                if (_cccWindow != null)
                {
                    // Bring target window to foreground
                    try
                    {
                        _cccWindow.SetForeground();
                        await Task.Delay(100, cancellationToken);
                        StatusChanged?.Invoke(this, $"Pasting to: {_cccWindow.Name}");
                    }
                    catch { }

                    // Use keyboard input, keeping target window focused
                    await PasteUsingKeyboardAsync(rows, cancellationToken);
                }
                else
                {
                    // No window selected - paste to CURRENT focused window
                    // This is like the old AutoHotkey behavior but user initiated it
                    StatusChanged?.Invoke(this, "No target selected. Pasting to current window...");
                    await PasteToCurrentWindowAsync(rows, cancellationToken);
                }

                processedRows = totalRows;
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Paste cancelled");
                success = false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                success = false;
            }

            PasteCompleted?.Invoke(this, new CCCPasteCompletedEventArgs(processedRows, success));
        }

        /// <summary>
        /// Set the target window for pasting (user selected)
        /// </summary>
        public void SetTargetWindow(FlaWindow? window)
        {
            _cccWindow = window;
            if (window != null)
            {
                StatusChanged?.Invoke(this, $"Target set: {window.Name}");
            }
            else
            {
                StatusChanged?.Invoke(this, "Target cleared - will paste to current window");
            }
        }

        /// <summary>
        /// Get the currently selected target window
        /// </summary>
        public FlaWindow? GetTargetWindow() => _cccWindow;

        /// <summary>
        /// Get target window title (for display)
        /// </summary>
        public string GetTargetWindowTitle() => _cccWindow?.Name ?? "(Current Window)";

        /// <summary>
        /// Clear the target window (paste will go to current focused window)
        /// </summary>
        public void ClearTargetWindow()
        {
            _cccWindow = null;
            StatusChanged?.Invoke(this, "Target cleared");
        }

        /// <summary>
        /// Paste to current focused window (no target set)
        /// Like AutoHotkey - blind paste to whatever is focused
        /// </summary>
        private async Task PasteToCurrentWindowAsync(List<List<string>> rows, CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke(this, "Pasting to current window (no target lock)...");

            int totalRows = rows.Count;
            int currentRow = 0;

            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested) break;

                currentRow++;
                ProgressChanged?.Invoke(this, new CCCPasteProgressEventArgs(currentRow, totalRows));

                await TypeRowAsync(row, cancellationToken);

                // Press Enter to move to next row
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
                await Task.Delay(120, cancellationToken);
            }
        }

        /// <summary>
        /// Paste using UI Automation (direct value setting)
        /// </summary>
        private async Task PasteUsingAutomationAsync(List<List<string>> rows, CancellationToken cancellationToken)
        {
            if (_dataGrid == null) return;

            StatusChanged?.Invoke(this, "Using UI Automation (direct mode)...");

            int totalRows = rows.Count;
            int currentRow = 0;

            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested) break;

                currentRow++;
                ProgressChanged?.Invoke(this, new CCCPasteProgressEventArgs(currentRow, totalRows));

                // Try to find row cells in the data grid
                var cells = _dataGrid.FindAllChildren(cf =>
                    cf.ByControlType(ControlType.Edit)
                    .Or(cf.ByControlType(ControlType.Custom)));

                if (cells.Length >= row.Count)
                {
                    // Set values directly
                    for (int i = 0; i < row.Count && i < cells.Length; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        try
                        {
                            var textBox = cells[i].AsTextBox();
                            if (textBox != null && !string.IsNullOrEmpty(row[i]))
                            {
                                textBox.Text = row[i];
                            }
                        }
                        catch
                        {
                            // If direct setting fails, try typing
                            cells[i].Focus();
                            await Task.Delay(20, cancellationToken);
                            Keyboard.Type(row[i]);
                        }
                    }
                }
                else
                {
                    // Fallback to keyboard for this row
                    await TypeRowAsync(row, cancellationToken);
                }

                // Press Enter to move to next row
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
                await Task.Delay(100, cancellationToken);
            }
        }

        /// <summary>
        /// Paste using keyboard simulation (but still targeting CCC window)
        /// </summary>
        private async Task PasteUsingKeyboardAsync(List<List<string>> rows, CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke(this, "Using keyboard simulation...");

            // Keep CCC in foreground during paste
            if (_cccWindow != null)
            {
                _cccWindow.SetForeground();
                await Task.Delay(200, cancellationToken);
            }

            int totalRows = rows.Count;
            int currentRow = 0;

            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested) break;

                currentRow++;
                StatusChanged?.Invoke(this, $"Row {currentRow} of {totalRows}...");
                ProgressChanged?.Invoke(this, new CCCPasteProgressEventArgs(currentRow, totalRows));

                // Ensure CCC still has focus (re-focus if needed)
                if (_cccWindow != null)
                {
                    var foreground = GetForegroundWindow();
                    if (foreground != _cccWindow.Properties.NativeWindowHandle.ValueOrDefault)
                    {
                        _cccWindow.SetForeground();
                        await Task.Delay(50, cancellationToken);
                    }
                }

                await TypeRowAsync(row, cancellationToken);

                // Press Enter to move to next row
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
                await Task.Delay(120, cancellationToken);
            }
        }

        /// <summary>
        /// Type a single row with Tab between fields
        /// </summary>
        private async Task TypeRowAsync(List<string> row, CancellationToken cancellationToken)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var field = row[i];

                // Type the field value
                if (!string.IsNullOrEmpty(field))
                {
                    Keyboard.Type(field);
                    await Task.Delay(20, cancellationToken);
                }

                // Tab to next field
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
                await Task.Delay(30, cancellationToken);
            }
        }

        /// <summary>
        /// Inspect CCC window structure (for debugging/mapping)
        /// </summary>
        public string InspectCCCWindow(int maxDepth = 4)
        {
            if (_cccWindow == null)
            {
                if (!ConnectToCCC())
                    return "Could not connect to CCC ONE";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {_cccWindow!.Name} ===");
            sb.AppendLine($"Process: {_cccWindow.Properties.ProcessId.ValueOrDefault}");
            sb.AppendLine();

            InspectElement(_cccWindow, sb, 0, maxDepth);
            return sb.ToString();
        }

        private void InspectElement(AutomationElement element, System.Text.StringBuilder sb, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            var indent = new string(' ', depth * 2);
            var controlType = element.Properties.ControlType.ValueOrDefault;
            var name = element.Properties.Name.ValueOrDefault ?? "";
            var automationId = element.Properties.AutomationId.ValueOrDefault ?? "";

            // Show interactive or named elements
            if (controlType == ControlType.Edit || controlType == ControlType.Button ||
                controlType == ControlType.ComboBox || controlType == ControlType.DataGrid ||
                controlType == ControlType.Table || controlType == ControlType.List ||
                !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(automationId))
            {
                var nameTrunc = name.Length > 40 ? name[..40] + "..." : name;
                sb.AppendLine($"{indent}[{controlType}] \"{nameTrunc}\" ID={automationId}");
            }

            try
            {
                var children = element.FindAllChildren();
                foreach (var child in children)
                {
                    InspectElement(child, sb, depth + 1, maxDepth);
                }
            }
            catch { }
        }

        // P/Invoke for getting foreground window
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public void Dispose()
        {
            if (!_disposed)
            {
                _automation?.Dispose();
                _disposed = true;
            }
        }
    }

    public class WindowCandidate
    {
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int ProcessId { get; set; }
        public FlaWindow? Window { get; set; }
        public bool IsEstimatingApp { get; set; }

        public string DisplayName => $"{Title} ({ProcessName})";
    }

    public class CCCPasteProgressEventArgs : EventArgs
    {
        public int CurrentRow { get; }
        public int TotalRows { get; }
        public double PercentComplete => TotalRows > 0 ? (double)CurrentRow / TotalRows * 100 : 0;

        public CCCPasteProgressEventArgs(int currentRow, int totalRows)
        {
            CurrentRow = currentRow;
            TotalRows = totalRows;
        }
    }

    public class CCCPasteCompletedEventArgs : EventArgs
    {
        public int RowsProcessed { get; }
        public bool Success { get; }

        public CCCPasteCompletedEventArgs(int rowsProcessed, bool success)
        {
            RowsProcessed = rowsProcessed;
            Success = success;
        }
    }
}
