#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace McstudDesktop.Services
{
    /// <summary>
    /// CCC Insert Service - SIMPLIFIED keyboard automation
    ///
    /// SIMPLE APPROACH:
    /// 1. Right-click to open context menu
    /// 2. Type 'I' to jump to Insert Line (Windows standard menu behavior)
    /// 3. Press Enter
    /// 4. Type fields with Tab between them
    /// 5. Press Enter to confirm
    /// </summary>
    public class CCCInsertService
    {
        #region Singleton

        private static CCCInsertService? _instance;
        private static readonly object _lock = new object();

        public static CCCInsertService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new CCCInsertService();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect); // null to unlock

        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const int INPUT_KEYBOARD = 1;

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        private const byte VK_TAB = 0x09;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_ALT = 0x12;

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

        #endregion

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<InsertProgressArgs>? ProgressChanged;
        public event EventHandler<bool>? InsertCompleted;

        // State
        private IntPtr _targetWindow = IntPtr.Zero;
        private string _targetWindowTitle = "";
        private bool _isInserting = false;
        private CancellationTokenSource? _cts;

        // Cursor position tracking
        private int _savedCursorX = 0;
        private int _savedCursorY = 0;
        private bool _hasSavedCursor = false;

        // Click position - set when user clicks in CCC (mouse button down)
        private int _clickX = 0;
        private int _clickY = 0;
        private bool _hasClickPosition = false;

        // Timing settings (milliseconds) - FAST like AutoHotkey
        private int _menuDelay = 80;         // Wait for context menu
        private int _afterInsertDelay = 100; // After selecting Insert Line
        private int _tabDelay = 15;          // After Tab key
        private int _enterDelay = 50;        // After Enter key
        private int _charDelay = 1;          // Between characters (nearly instant)
        private int _betweenRowDelay = 100;  // Extra delay between rows

        // Known operation types
        private static readonly string[] OPERATION_TYPES = new[]
        {
            "Rpr", "Replace", "R&I", "R+I", "Blend", "Refinish", "O/H", "Sublet",
            "Add", "Remove", "Install", "Repair", "Overhaul"
        };

        public bool IsInserting => _isInserting;
        public string TargetWindowTitle => _targetWindowTitle;
        public bool HasClickPosition => _hasClickPosition;
        public IntPtr TargetWindow => _targetWindow;

        /// <summary>
        /// Set timing delays
        /// </summary>
        public void SetDelays(int tabDelayMs, int enterDelayMs, int initialDelayMs)
        {
            _tabDelay = Math.Max(10, tabDelayMs);
            _enterDelay = Math.Max(50, enterDelayMs);
            _menuDelay = Math.Max(100, initialDelayMs);
        }

        /// <summary>
        /// Set target window and save cursor position
        /// </summary>
        public void SetTargetWindow(IntPtr hwnd)
        {
            _targetWindow = hwnd;
            _targetWindowTitle = GetWindowTitle(hwnd);

            if (GetCursorPos(out POINT pt))
            {
                _savedCursorX = pt.X;
                _savedCursorY = pt.Y;
                _hasSavedCursor = true;
            }

            StatusChanged?.Invoke(this, $"Target: {_targetWindowTitle}");
        }

        /// <summary>
        /// Set target window WITHOUT saving cursor position
        /// Used to track which window user is in while cursor position is saved separately
        /// </summary>
        public void SetTargetWindowOnly(IntPtr hwnd)
        {
            _targetWindow = hwnd;
            _targetWindowTitle = GetWindowTitle(hwnd);
        }

        /// <summary>
        /// Save cursor position (continuous tracking while in external window)
        /// </summary>
        public void SaveCursorPosition()
        {
            if (GetCursorPos(out POINT pt))
            {
                _savedCursorX = pt.X;
                _savedCursorY = pt.Y;
                _hasSavedCursor = true;
            }
        }

        /// <summary>
        /// Lock in the current cursor position as the click position.
        /// Call this when user transitions from external window to McStud.
        /// This captures where they LAST were in the external window.
        /// </summary>
        public void LockClickPosition()
        {
            if (_hasSavedCursor)
            {
                _clickX = _savedCursorX;
                _clickY = _savedCursorY;
                _hasClickPosition = true;
                System.Diagnostics.Debug.WriteLine($"[CCC] Click position LOCKED at ({_clickX}, {_clickY})");
            }
        }

        /// <summary>
        /// Get the locked click position
        /// </summary>
        public (int x, int y, bool hasPosition) GetClickPosition()
        {
            return (_clickX, _clickY, _hasClickPosition);
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// Force window activation
        /// </summary>
        private bool ForceActivateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                var currentForeground = GetForegroundWindow();
                var foregroundThread = GetWindowThreadProcessId(currentForeground, out _);
                var ourThread = GetCurrentThreadId();

                if (ourThread != foregroundThread)
                {
                    AttachThreadInput(ourThread, foregroundThread, true);
                }

                keybd_event(VK_ALT, 0, 0, UIntPtr.Zero);
                keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                ShowWindow(hwnd, SW_RESTORE);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);

                if (ourThread != foregroundThread)
                {
                    AttachThreadInput(ourThread, foregroundThread, false);
                }

                Thread.Sleep(100);
                return GetForegroundWindow() == hwnd;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Send a key press
        /// </summary>
        private void SendKeyPress(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            Thread.Sleep(10);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>
        /// Type a single character using Unicode input
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
        /// Type text with delay between characters
        /// </summary>
        private void TypeText(string text)
        {
            foreach (char c in text)
            {
                TypeCharacter(c);
                if (_charDelay > 0)
                    Thread.Sleep(_charDelay);
            }
        }

        /// <summary>
        /// Right-click at current cursor position
        /// </summary>
        private void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        /// <summary>
        /// Left-click at current cursor position
        /// </summary>
        private void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        /// <summary>
        /// Parse a raw tab-separated row and extract the meaningful fields.
        ///
        /// Excel format (with operation): 0 0 0 0 0 0 Replace 0 Description 0 Qty Price 0 0 0 Labor 0 0
        /// Excel format (Add/no-op):      0 0 0 0 0 0 0       0 Description 0 Qty Price 0 0 0 Labor 0 0
        ///
        /// Key insight: The structure is consistent - operation OR 0, then 0, then description, then data fields
        /// </summary>
        private (string operation, string description, string qty, string price, string labor) ParseRow(string row)
        {
            var parts = row.Split('\t');
            System.Diagnostics.Debug.WriteLine($"[PARSE] Parts count: {parts.Length}");
            System.Diagnostics.Debug.WriteLine($"[PARSE] Row: {row}");

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
            System.Diagnostics.Debug.WriteLine($"[PARSE] Operation '{operation}' at index {opIndex}");

            // Find description - it's the first TEXT field (non-empty, non-zero, non-numeric)
            // Search from beginning, skip numeric fields and operation
            string description = "";
            int descIndex = -1;
            int searchStart = opIndex >= 0 ? opIndex + 1 : 0;

            for (int i = searchStart; i < parts.Length; i++)
            {
                var val = parts[i].Trim();
                if (string.IsNullOrEmpty(val) || val == "0") continue;
                if (decimal.TryParse(val, out _)) continue;
                // Skip if it's an operation type
                bool isOp = false;
                foreach (var op in OPERATION_TYPES)
                {
                    if (val.Equals(op, StringComparison.OrdinalIgnoreCase)) { isOp = true; break; }
                }
                if (isOp) continue;

                description = val;
                descIndex = i;
                break;
            }
            System.Diagnostics.Debug.WriteLine($"[PARSE] Description '{description}' at index {descIndex}");

            // Extract numeric values - scan ALL fields after description for qty, price, labor
            string qty = "1";
            string price = "";
            string labor = "";

            if (descIndex >= 0)
            {
                // Collect all numeric values after description
                var numericValues = new List<(int index, decimal value)>();
                for (int i = descIndex + 1; i < parts.Length; i++)
                {
                    var val = parts[i].Trim();
                    if (decimal.TryParse(val, out decimal num) && num > 0)
                    {
                        numericValues.Add((i, num));
                        System.Diagnostics.Debug.WriteLine($"[PARSE] Found numeric at {i}: {num}");
                    }
                }

                // First non-zero = Qty, Second = Price, find Labor (usually has decimal like 0.8, 1.5)
                if (numericValues.Count > 0)
                {
                    // First value is usually Qty (whole number like 1, 2)
                    qty = ((int)numericValues[0].value).ToString();
                }
                if (numericValues.Count > 1)
                {
                    // Second value is usually Price
                    price = numericValues[1].value.ToString("0.##");
                }
                // Labor is typically the value with decimals (0.5, 0.8, 1.5, etc) after price
                for (int i = 2; i < numericValues.Count; i++)
                {
                    var val = numericValues[i].value;
                    // Labor values are typically small decimals (0.1 to 10.0)
                    if (val < 50 && val != (int)val) // Has decimal part and less than 50
                    {
                        labor = val.ToString("0.#");
                        break;
                    }
                    else if (val < 20) // Whole number labor hours (1, 2, etc)
                    {
                        labor = val.ToString("0.#");
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[PARSE] Qty='{qty}', Price='{price}', Labor='{labor}'");
            }

            System.Diagnostics.Debug.WriteLine($"[PARSE] RESULT: Op='{operation}', Desc='{description}', Qty='{qty}', Price='{price}', Labor='{labor}'");

            return (operation, description, qty, price, labor);
        }

        /// <summary>
        /// MAIN METHOD: Insert raw rows into CCC
        ///
        /// APPROACH:
        /// 1. FIRST activate the CCC window using Win32 API
        /// 2. Move cursor to saved position in CCC
        /// 3. Click to ensure CCC has focus
        /// 4. For each row: Right-click > I > Enter > Type fields with Tab > Enter
        /// </summary>
        public async Task InsertRawRowsAsync(List<string> rawRows, CancellationToken cancellationToken = default)
        {
            if (rawRows.Count == 0)
            {
                StatusChanged?.Invoke(this, "No rows to insert");
                return;
            }

            if (_targetWindow == IntPtr.Zero || !_hasClickPosition)
            {
                StatusChanged?.Invoke(this, "Click in CCC first where you want to insert.");
                return;
            }

            _isInserting = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            int totalRows = rawRows.Count;
            int successCount = 0;

            try
            {
                StatusChanged?.Invoke(this, $"Starting insert into CCC...");
                System.Diagnostics.Debug.WriteLine($"[CCC] Target window: {_targetWindow} - {_targetWindowTitle}");
                System.Diagnostics.Debug.WriteLine($"[CCC] Click position: ({_clickX}, {_clickY})");

                // === STEP 0: Activate the CCC window ===
                System.Diagnostics.Debug.WriteLine($"[CCC] Activating target window: {_targetWindow}");
                bool activated = ForceActivateWindow(_targetWindow);
                System.Diagnostics.Debug.WriteLine($"[CCC] Window activation result: {activated}");

                // Brief wait for window to activate
                await Task.Delay(50, _cts.Token);

                // === STEP 1: Move cursor to LOCKED click position ===
                SetCursorPos(_clickX, _clickY);
                await Task.Delay(50, _cts.Token);

                // === LOCK CURSOR IMMEDIATELY before clicking ===
                // This prevents any mouse movement from interfering
                var lockRect = new RECT
                {
                    Left = _clickX,
                    Top = _clickY,
                    Right = _clickX + 1,
                    Bottom = _clickY + 1
                };
                ClipCursor(ref lockRect);
                System.Diagnostics.Debug.WriteLine($"[CCC] Cursor locked at ({_clickX}, {_clickY})");

                // Block all user input during insert (requires admin, but try anyway)
                BlockInput(true);
                System.Diagnostics.Debug.WriteLine($"[CCC] Input blocked");

                // Click to ensure focus is in the right spot within CCC
                LeftClick();
                await Task.Delay(150, _cts.Token);

                // Verify CCC has focus now
                var currentFg = GetForegroundWindow();
                System.Diagnostics.Debug.WriteLine($"[CCC] After click, foreground window: {currentFg}");

                StatusChanged?.Invoke(this, $"Inserting {totalRows} rows...");

                // REVERSE the rows because Insert Line inserts ABOVE the current position
                // So we insert last row first, then second-to-last, etc.
                var reversedRows = new List<string>(rawRows);
                reversedRows.Reverse();

                // Process each row (in reverse order so they end up in correct sequence)
                for (int i = 0; i < reversedRows.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var row = reversedRows[i];
                    var (operation, description, qty, price, labor) = ParseRow(row);

                    // Skip rows with no meaningful data
                    if (string.IsNullOrWhiteSpace(operation) && string.IsNullOrWhiteSpace(description))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CCC] Skipping row {i+1} - no data");
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"[CCC] Row {i+1}: Op={operation}, Desc={description}, Qty={qty}, Price={price}, Labor={labor}");

                    ProgressChanged?.Invoke(this, new InsertProgressArgs(i + 1, totalRows));
                    StatusChanged?.Invoke(this, $"Row {i + 1}/{totalRows}: {operation} - {(description.Length > 30 ? description.Substring(0, 30) + "..." : description)}");

                    // === STEP 1: Open context menu ===
                    System.Diagnostics.Debug.WriteLine($"[CCC] Right-click for context menu...");
                    RightClick();
                    await Task.Delay(_menuDelay, _cts.Token);

                    // === STEP 2: Type 'I' to select Insert Line ===
                    System.Diagnostics.Debug.WriteLine($"[CCC] Typing 'I' for Insert Line...");
                    TypeCharacter('I');
                    await Task.Delay(100, _cts.Token);

                    // === STEP 3: Press Enter to confirm Insert Line selection ===
                    System.Diagnostics.Debug.WriteLine($"[CCC] Enter to select Insert Line...");
                    SendKeyPress(VK_RETURN);
                    await Task.Delay(_afterInsertDelay, _cts.Token);

                    // === STEP 4: Now we're in the Insert Line row ===
                    // CCC Desktop line item fields (after Insert Line):
                    // Field 1: Operation Type (Replace, R&I, etc.)
                    // Field 2: Description
                    // Field 3: Qty
                    // Field 4: Price/Amount
                    // Field 5: Labor Hours
                    //
                    // We type value then Tab to next field

                    System.Diagnostics.Debug.WriteLine($"[CCC] === TYPING SEQUENCE ===");
                    System.Diagnostics.Debug.WriteLine($"[CCC] Will type: Op='{operation}' -> Desc='{description}' -> Qty='{qty}' -> Price='{price}' -> Labor='{labor}'");

                    // Field 1: Operation
                    System.Diagnostics.Debug.WriteLine($"[CCC] Field 1 (Operation): '{operation}'");
                    if (!string.IsNullOrEmpty(operation))
                    {
                        TypeText(operation);
                    }
                    SendKeyPress(VK_TAB);
                    await Task.Delay(_tabDelay, _cts.Token);

                    // Field 2: Description
                    System.Diagnostics.Debug.WriteLine($"[CCC] Field 2 (Description): '{description}'");
                    if (!string.IsNullOrEmpty(description))
                    {
                        TypeText(description);
                    }
                    SendKeyPress(VK_TAB);
                    await Task.Delay(_tabDelay, _cts.Token);

                    // Field 3: Quantity
                    System.Diagnostics.Debug.WriteLine($"[CCC] Field 3 (Qty): '{qty}'");
                    if (!string.IsNullOrEmpty(qty))
                    {
                        TypeText(qty);
                    }
                    SendKeyPress(VK_TAB);
                    await Task.Delay(_tabDelay, _cts.Token);

                    // Field 4: Price
                    System.Diagnostics.Debug.WriteLine($"[CCC] Field 4 (Price): '{price}'");
                    if (!string.IsNullOrEmpty(price))
                    {
                        TypeText(price);
                    }
                    SendKeyPress(VK_TAB);
                    await Task.Delay(_tabDelay, _cts.Token);

                    // Field 5: Labor Hours
                    System.Diagnostics.Debug.WriteLine($"[CCC] Field 5 (Labor): '{labor}'");
                    if (!string.IsNullOrEmpty(labor))
                    {
                        TypeText(labor);
                    }

                    // === STEP 5: Press Enter to confirm the row ===
                    System.Diagnostics.Debug.WriteLine($"[CCC] Enter to confirm row...");
                    SendKeyPress(VK_RETURN);
                    await Task.Delay(_enterDelay, _cts.Token);

                    // Extra delay between rows for CCC to finish processing
                    System.Diagnostics.Debug.WriteLine($"[CCC] Row {i+1} complete, waiting before next row...");
                    await Task.Delay(_betweenRowDelay, _cts.Token);

                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"[CCC] Successfully inserted row {i+1}");
                }

                // Final status
                if (successCount == totalRows)
                {
                    StatusChanged?.Invoke(this, $"Done! Inserted {totalRows} rows.");
                    InsertCompleted?.Invoke(this, true);
                }
                else if (successCount > 0)
                {
                    StatusChanged?.Invoke(this, $"Inserted {successCount}/{totalRows} rows.");
                    InsertCompleted?.Invoke(this, true);
                }
                else
                {
                    StatusChanged?.Invoke(this, $"No rows inserted.");
                    InsertCompleted?.Invoke(this, false);
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, $"Cancelled. Inserted {successCount} rows.");
                InsertCompleted?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CCC] Exception: {ex}");
                InsertCompleted?.Invoke(this, false);
            }
            finally
            {
                // === UNBLOCK INPUT - always unblock even if error/cancel ===
                BlockInput(false);
                System.Diagnostics.Debug.WriteLine($"[CCC] Input unblocked");

                // === UNLOCK CURSOR - always unlock even if error/cancel ===
                ClipCursor(IntPtr.Zero);
                System.Diagnostics.Debug.WriteLine($"[CCC] Cursor unlocked");
                _isInserting = false;
            }
        }

        /// <summary>
        /// Cancel ongoing operation
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
        }
    }

    public class InsertProgressArgs : EventArgs
    {
        public int Current { get; }
        public int Total { get; }
        public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;

        public InsertProgressArgs(int current, int total)
        {
            Current = current;
            Total = total;
        }
    }
}
