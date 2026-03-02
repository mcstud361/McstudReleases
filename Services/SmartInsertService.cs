#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Smart Insert Service - Uses UI Automation to find and click "Insert Line" menu item
    ///
    /// This is smarter than blind keyboard navigation because:
    /// 1. Finds the menu item by text regardless of position
    /// 2. Works even if menu items change based on row content
    /// 3. Can detect if menu item exists/is enabled
    /// 4. More reliable than counting arrow key presses
    /// </summary>
    public class SmartInsertService : IDisposable
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
            public uint padding1;
            public uint padding2;
        }

        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const int INPUT_KEYBOARD = 1;

        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_TAB = 0x09;
        private const byte VK_DOWN = 0x28;

        #endregion

        private UIA3Automation? _automation;
        private bool _disposed;
        private bool _isPasting;
        private CancellationTokenSource? _cts;

        // Menu item variations to search for
        private static readonly string[] INSERT_LINE_TEXTS = new[]
        {
            "Insert Line",
            "Insert Line...",
            "Insert",
            "Insert Row",
            "Add Line",
            "New Line"
        };

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<SmartInsertProgressArgs>? ProgressChanged;
        public event EventHandler<bool>? InsertCompleted;

        public bool IsPasting => _isPasting;

        // Speed settings
        private int _menuDelay = 100;      // Wait for menu to appear
        private int _afterInsertDelay = 50; // Wait after insert line clicked
        private int _tabDelay = 15;         // Between fields
        private int _enterDelay = 50;       // After each row

        public SmartInsertService()
        {
            _automation = new UIA3Automation();
        }

        /// <summary>
        /// Set speed delays
        /// </summary>
        public void SetDelays(int menuDelayMs, int afterInsertMs, int tabDelayMs, int enterDelayMs)
        {
            _menuDelay = Math.Max(50, menuDelayMs);
            _afterInsertDelay = Math.Max(20, afterInsertMs);
            _tabDelay = Math.Max(5, tabDelayMs);
            _enterDelay = Math.Max(10, enterDelayMs);
        }

        /// <summary>
        /// Smart insert with menu detection
        /// For each row:
        /// 1. Right-click at current position
        /// 2. Use UI Automation to find "Insert Line" menu item
        /// 3. Click it
        /// 4. Type the row data
        /// </summary>
        public async Task SmartInsertAsync(List<List<string>> rows, CancellationToken cancellationToken = default)
        {
            if (rows.Count == 0)
            {
                StatusChanged?.Invoke(this, "No data to insert");
                return;
            }

            _isPasting = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            int totalRows = rows.Count;
            int successCount = 0;

            try
            {
                StatusChanged?.Invoke(this, $"Smart inserting {totalRows} rows...");

                for (int i = 0; i < rows.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var row = rows[i];
                    StatusChanged?.Invoke(this, $"Inserting row {i + 1} of {totalRows}...");
                    ProgressChanged?.Invoke(this, new SmartInsertProgressArgs(i + 1, totalRows));

                    // Step 1: Right-click at current cursor position
                    RightClick();
                    await Task.Delay(_menuDelay, _cts.Token);

                    // Step 2: Find and click "Insert Line" using UI Automation
                    bool found = await FindAndClickInsertLineAsync(_cts.Token);

                    if (!found)
                    {
                        // Fallback: Try keyboard navigation (type 'I' to jump to Insert)
                        StatusChanged?.Invoke(this, "Menu item not found, trying keyboard...");
                        found = await TryKeyboardFallbackAsync(_cts.Token);
                    }

                    if (!found)
                    {
                        // Close the menu and abort
                        SendKeyPress(VK_ESCAPE);
                        StatusChanged?.Invoke(this, "Could not find Insert Line menu item. Aborting.");
                        break;
                    }

                    // Wait for insert line to complete
                    await Task.Delay(_afterInsertDelay, _cts.Token);

                    // Step 3: Type the row data
                    await TypeRowAsync(row, _cts.Token);

                    // Step 4: Press Enter to confirm the row
                    SendKeyPress(VK_RETURN);
                    await Task.Delay(_enterDelay, _cts.Token);

                    successCount++;
                }

                StatusChanged?.Invoke(this, $"Done! Inserted {successCount} of {totalRows} rows.");
                InsertCompleted?.Invoke(this, successCount == totalRows);
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, $"Cancelled. Inserted {successCount} rows.");
                InsertCompleted?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                InsertCompleted?.Invoke(this, false);
            }
            finally
            {
                _isPasting = false;
            }
        }

        /// <summary>
        /// Find the context menu and look for "Insert Line" item using UI Automation
        /// </summary>
        private async Task<bool> FindAndClickInsertLineAsync(CancellationToken ct)
        {
            if (_automation == null) return false;

            try
            {
                // Wait a bit for menu to fully render
                await Task.Delay(50, ct);

                // Get all windows on desktop - context menus appear as separate windows
                var desktop = _automation.GetDesktop();

                // Look for menu windows
                var menus = desktop.FindAllChildren(cf =>
                    cf.ByControlType(ControlType.Menu)
                    .Or(cf.ByControlType(ControlType.List))
                    .Or(cf.ByClassName("#32768"))); // Win32 context menu class

                foreach (var menu in menus)
                {
                    // Search this menu for Insert Line item
                    var menuItem = FindInsertLineItem(menu);
                    if (menuItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SmartInsert] Found Insert Line: {menuItem.Name}");

                        // Check if enabled
                        if (menuItem.Properties.IsEnabled.ValueOrDefault)
                        {
                            // Click it!
                            try
                            {
                                // Try invoke pattern first (most reliable)
                                var invokePattern = menuItem.Patterns.Invoke.PatternOrDefault;
                                if (invokePattern != null)
                                {
                                    invokePattern.Invoke();
                                    return true;
                                }

                                // Fallback to click
                                menuItem.Click();
                                return true;
                            }
                            catch
                            {
                                // If clicking fails, try keyboard Enter
                                menuItem.Focus();
                                await Task.Delay(20, ct);
                                SendKeyPress(VK_RETURN);
                                return true;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[SmartInsert] Insert Line is disabled");
                        }
                    }
                }

                // Also search for popup windows (some menus appear as popups)
                var popups = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
                foreach (var popup in popups)
                {
                    // Check if this looks like a context menu (small, no title, has menu items)
                    var bounds = popup.BoundingRectangle;
                    if (bounds.Width > 0 && bounds.Width < 400 && bounds.Height > 0 && bounds.Height < 600)
                    {
                        var menuItem = FindInsertLineItem(popup);
                        if (menuItem != null && menuItem.Properties.IsEnabled.ValueOrDefault)
                        {
                            try
                            {
                                var invokePattern = menuItem.Patterns.Invoke.PatternOrDefault;
                                if (invokePattern != null)
                                {
                                    invokePattern.Invoke();
                                    return true;
                                }
                                menuItem.Click();
                                return true;
                            }
                            catch
                            {
                                menuItem.Focus();
                                await Task.Delay(20, ct);
                                SendKeyPress(VK_RETURN);
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartInsert] Error finding menu: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Search an element tree for Insert Line menu item
        /// </summary>
        private AutomationElement? FindInsertLineItem(AutomationElement parent)
        {
            try
            {
                // Get all descendants that could be menu items
                var candidates = parent.FindAllDescendants(cf =>
                    cf.ByControlType(ControlType.MenuItem)
                    .Or(cf.ByControlType(ControlType.ListItem))
                    .Or(cf.ByControlType(ControlType.Button))
                    .Or(cf.ByControlType(ControlType.Text)));

                foreach (var candidate in candidates)
                {
                    var name = candidate.Name ?? "";

                    // Check against our list of possible names
                    foreach (var insertText in INSERT_LINE_TEXTS)
                    {
                        if (name.Contains(insertText, StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }

                // Also check direct children
                var children = parent.FindAllChildren();
                foreach (var child in children)
                {
                    var name = child.Name ?? "";
                    foreach (var insertText in INSERT_LINE_TEXTS)
                    {
                        if (name.Contains(insertText, StringComparison.OrdinalIgnoreCase))
                        {
                            return child;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartInsert] Error searching menu: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Fallback: Use keyboard to navigate to Insert Line
        /// Strategy: Press 'I' key to jump to first item starting with I
        /// </summary>
        private async Task<bool> TryKeyboardFallbackAsync(CancellationToken ct)
        {
            try
            {
                // Type 'I' to jump to Insert Line (most menus support this)
                TypeCharacter('I');
                await Task.Delay(50, ct);

                // Press Enter to select
                SendKeyPress(VK_RETURN);
                await Task.Delay(30, ct);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Right-click at current cursor position
        /// </summary>
        private void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        /// <summary>
        /// Left-click at current cursor position
        /// </summary>
        private void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        /// <summary>
        /// Send a single key press
        /// </summary>
        private void SendKeyPress(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>
        /// Type a single character
        /// </summary>
        private void TypeCharacter(char c)
        {
            var inputs = new INPUT[2];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = 0;
            inputs[0].ki.wScan = (ushort)c;
            inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = 0;
            inputs[1].ki.wScan = (ushort)c;
            inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Type text using SendInput (Unicode)
        /// </summary>
        private void TypeText(string text)
        {
            foreach (char c in text)
            {
                TypeCharacter(c);
            }
        }

        /// <summary>
        /// Type a row of data (field, tab, field, tab, etc.)
        /// </summary>
        private async Task TypeRowAsync(List<string> fields, CancellationToken ct)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var value = fields[i];

                // Type the value if not empty
                if (!string.IsNullOrEmpty(value))
                {
                    TypeText(value);
                }

                // Tab to next field (but NOT after last field)
                if (i < fields.Count - 1)
                {
                    SendKeyPress(VK_TAB);
                    await Task.Delay(_tabDelay, ct);
                }
            }
        }

        // Known operation types for parsing
        private static readonly string[] OPERATION_TYPES = new[]
        {
            "Rpr", "Replace", "R&I", "R+I", "Blend", "Refinish", "O/H", "Sublet",
            "Add", "Remove", "Install", "Repair", "Overhaul"
        };

        /// <summary>
        /// Parse a raw tab-separated row and extract the meaningful fields.
        /// Returns: (operation, description, quantity, price, laborHours)
        /// </summary>
        private (string operation, string description, string qty, string price, string labor) ParseRow(string row)
        {
            var parts = row.Split('\t');

            // Find operation type
            int opIndex = -1;
            string operation = "";
            for (int i = 0; i < parts.Length; i++)
            {
                var val = parts[i].Trim();
                foreach (var op in OPERATION_TYPES)
                {
                    if (val.Equals(op, StringComparison.OrdinalIgnoreCase))
                    {
                        opIndex = i;
                        operation = val;
                        break;
                    }
                }
                if (opIndex >= 0) break;
            }

            // Find description (first non-empty, non-zero, non-numeric text after operation)
            string description = "";
            int descIndex = -1;
            if (opIndex >= 0)
            {
                for (int i = opIndex + 1; i < parts.Length; i++)
                {
                    var val = parts[i].Trim();
                    if (string.IsNullOrEmpty(val) || val == "0") continue;
                    if (decimal.TryParse(val, out _)) continue;
                    if (val.Length > 2) // Description should be meaningful text
                    {
                        description = val;
                        descIndex = i;
                        break;
                    }
                }
            }

            // Find numbers after description: qty, price, labor hours
            var numbersAfterDesc = new List<decimal>();
            if (descIndex >= 0)
            {
                for (int i = descIndex + 1; i < parts.Length; i++)
                {
                    var val = parts[i].Trim();
                    if (decimal.TryParse(val, out decimal num) && num != 0)
                    {
                        numbersAfterDesc.Add(num);
                    }
                }
            }

            // Extract qty (first number), labor (last number), price (middle if present)
            string qty = "1";  // Default
            string price = "";
            string labor = "";

            if (numbersAfterDesc.Count >= 1)
            {
                qty = numbersAfterDesc[0].ToString("0");
            }
            if (numbersAfterDesc.Count >= 2)
            {
                labor = numbersAfterDesc[numbersAfterDesc.Count - 1].ToString("0.0");
            }
            if (numbersAfterDesc.Count >= 3)
            {
                // Price is typically the second number (between qty and labor)
                price = numbersAfterDesc[1].ToString("0.00");
            }

            return (operation, description, qty, price, labor);
        }

        /// <summary>
        /// Smart insert with UI Automation menu detection - accepts raw rows
        /// Parses each row to extract: Operation, Description, Qty, Price, Labor
        /// </summary>
        public async Task SmartInsertRawRowsAsync(List<string> rawRows, CancellationToken cancellationToken = default)
        {
            // Convert raw rows to parsed fields
            var parsedRows = new List<List<string>>();
            foreach (var row in rawRows)
            {
                var (operation, description, qty, price, labor) = ParseRow(row);
                var fields = new List<string> { operation, description, qty, price, labor };
                parsedRows.Add(fields);
                System.Diagnostics.Debug.WriteLine($"[SmartInsert] Parsed: Op={operation}, Desc={description}, Qty={qty}, Price={price}, Labor={labor}");
            }

            await SmartInsertAsync(parsedRows, cancellationToken);
        }

        /// <summary>
        /// Cancel ongoing operation
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _automation?.Dispose();
                _cts?.Dispose();
                _disposed = true;
            }
        }
    }

    public class SmartInsertProgressArgs : EventArgs
    {
        public int Current { get; }
        public int Total { get; }
        public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;

        public SmartInsertProgressArgs(int current, int total)
        {
            Current = current;
            Total = total;
        }
    }
}
