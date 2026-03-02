using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace McStudDesktop.Services;

/// <summary>
/// Simple typing service that types at the current cursor position
/// User positions cursor in target application, then this service types the data
/// with human-like delays between keystrokes
/// </summary>
public class TypeItService : IDisposable
{
    // Win32 API for keyboard input - using SendInput (modern, reliable)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // Legacy keybd_event kept for fallback if needed
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // SendInput structures
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    // Win32 API for mouse input
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    // Cursor APIs for "aiming mode"
    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern void SetCursorPos(int x, int y);

    // Window detection APIs
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private const uint GA_ROOT = 2; // Get root owner window

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // Cursor constants
    private const int IDC_CROSS = 32515;      // Crosshair cursor
    private const int IDC_UPARROW = 32516;    // Up arrow
    private const int IDC_HAND = 32649;       // Hand pointer
    private const int IDC_APPSTARTING = 32650; // Arrow + hourglass (shows "busy but can click")
    private const uint OCR_NORMAL = 32512;    // Normal arrow cursor ID
    private const uint SPI_SETCURSORS = 0x0057;

    // For restoring cursor
    private IntPtr _originalCursor = IntPtr.Zero;
    private bool _cursorChanged = false;

    // BlockInput API - blocks ALL keyboard and mouse input (requires admin, or we use fallback)
    [DllImport("user32.dll")]
    private static extern bool BlockInput(bool fBlockIt);

    // Clip cursor to a region (prevents mouse from moving during automation)
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect); // null to unclip

    [DllImport("user32.dll")]
    private static extern bool GetClipCursor(out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    // Low-level hooks (no admin required) - used as fallback for blocking
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int HC_ACTION = 0;

    // Flag to check if input is injected (programmatic)
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_MENU = 0x12; // Alt key
    private const byte VK_DOWN = 0x28; // Down arrow
    private const byte VK_UP = 0x26;   // Up arrow
    private const byte VK_CONTROL = 0x11; // Ctrl key
    private const byte VK_V = 0x56;    // V key (for Ctrl+V)

    // Mouse event flags
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private readonly Random _random = new();
    private bool _disposed;

    // Hook handles
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private LowLevelProc? _keyboardProc;
    private LowLevelProc? _mouseProc;
    private static bool _monitoringInput = false;

    // Input blocking mode (swallows all input during automation)
    private static bool _blockingInput = false;
    private bool _blockInputApiAvailable = false;
    private IntPtr _blockingKeyboardHook = IntPtr.Zero;
    private IntPtr _blockingMouseHook = IntPtr.Zero;
    private LowLevelProc? _blockingKeyboardProc;
    private LowLevelProc? _blockingMouseProc;

    // Aiming mode state
    private bool _inAimingMode = false;
    private bool _sawMouseDown = false;  // Track if we've seen button DOWN in aiming mode
    private DateTime _lastClickTime = DateTime.MinValue;  // For double-click detection
    private const int DOUBLE_CLICK_MS = 400;  // Max time between clicks for double-click
    private TaskCompletionSource<AimingResult>? _aimingTcs;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_KEYDOWN = 0x0100;
    private const byte VK_ESCAPE = 0x1B;

    // Cancellation for user interrupt
    private CancellationTokenSource? _userInterruptCts;

    // Track progress for resume capability
    private int _lastCompletedRow = -1;
    private string[][]? _currentRows;

    /// <summary>
    /// Event fired when user input is detected during automation
    /// </summary>
    public event EventHandler? UserInterrupted;

    /// <summary>
    /// Event fired when aiming mode starts (cursor should change)
    /// </summary>
    public event EventHandler? AimingModeStarted;

    /// <summary>
    /// Event fired when aiming mode ends (cursor should restore)
    /// </summary>
    public event EventHandler? AimingModeEnded;

    /// <summary>
    /// Event fired when input blocking starts
    /// </summary>
    public event EventHandler<bool>? InputBlockingChanged; // true = blocking, false = unblocked

    /// <summary>
    /// Returns true if input is currently being blocked
    /// </summary>
    public bool IsInputBlocked => _blockingInput;

    /// <summary>
    /// Minimum delay between keystrokes (ms)
    /// </summary>
    public int MinTypeDelay { get; set; } = 5;

    /// <summary>
    /// Maximum delay between keystrokes (ms)
    /// </summary>
    public int MaxTypeDelay { get; set; } = 15;

    /// <summary>
    /// Delay between fields (after Tab)
    /// </summary>
    public int FieldDelay { get; set; } = 20;

    /// <summary>
    /// Delay between rows (after Enter / after Insert Line)
    /// </summary>
    public int RowDelay { get; set; } = 50;

    /// <summary>
    /// Delay for menu to appear after right-click
    /// </summary>
    public int MenuDelay { get; set; } = 150;

    /// <summary>
    /// Delay after paste operation
    /// </summary>
    public int PasteDelay { get; set; } = 30;

    /// <summary>
    /// Delay after Insert Line operation
    /// </summary>
    public int InsertDelay { get; set; } = 50;

    /// <summary>
    /// Whether to block user input during automation (prevents accidental interference).
    /// When true, user cannot click or type until sequence completes.
    /// </summary>
    public bool BlockUserInput { get; set; } = true;

    /// <summary>
    /// Set all delays for a speed preset
    /// </summary>
    /// <param name="speedLevel">0=Slow, 1=Normal, 2=Fast, 3=Turbo, 4=Insane</param>
    public void SetSpeedLevel(int speedLevel)
    {
        switch (speedLevel)
        {
            case 0: // Slow - for debugging or slow systems
                MinTypeDelay = 20; MaxTypeDelay = 50;
                FieldDelay = 50; RowDelay = 100;
                MenuDelay = 300; PasteDelay = 100; InsertDelay = 150;
                break;
            case 1: // Normal
                MinTypeDelay = 10; MaxTypeDelay = 25;
                FieldDelay = 30; RowDelay = 75;
                MenuDelay = 200; PasteDelay = 50; InsertDelay = 75;
                break;
            case 2: // Fast (default)
                MinTypeDelay = 5; MaxTypeDelay = 15;
                FieldDelay = 20; RowDelay = 50;
                MenuDelay = 150; PasteDelay = 30; InsertDelay = 50;
                break;
            case 3: // Turbo
                MinTypeDelay = 3; MaxTypeDelay = 8;
                FieldDelay = 10; RowDelay = 20;
                MenuDelay = 100; PasteDelay = 15; InsertDelay = 25;
                break;
            case 4: // Insane - maximum speed (like AutoHotkey with 0 delay)
                MinTypeDelay = 1; MaxTypeDelay = 3;
                FieldDelay = 5; RowDelay = 10;
                MenuDelay = 80; PasteDelay = 8; InsertDelay = 15;
                break;
            default:
                SetSpeedLevel(3); // Default to Turbo
                break;
        }
        System.Diagnostics.Debug.WriteLine($"[TypeIt] Speed level set to {speedLevel}: MinTypeDelay={MinTypeDelay}, RowDelay={RowDelay}");
    }

    /// <summary>
    /// Position of "Insert Line" in CCC's right-click menu (0-based)
    /// Adjust based on actual menu structure
    /// </summary>
    public int InsertLineMenuPosition { get; set; } = 0;

    /// <summary>
    /// Whether to monitor for user input during automation.
    /// If user input detected, automation pauses and UserInterrupted event fires.
    /// </summary>
    public bool MonitorUserInput { get; set; } = true;

    /// <summary>
    /// Returns the last completed row index (0-based), or -1 if none completed
    /// </summary>
    public int LastCompletedRow => _lastCompletedRow;

    /// <summary>
    /// Returns the total rows in the current/last export
    /// </summary>
    public int TotalRows => _currentRows?.Length ?? 0;

    /// <summary>
    /// Returns true if there are remaining rows to export
    /// </summary>
    public bool CanResume => _currentRows != null && _lastCompletedRow < _currentRows.Length - 1;

    /// <summary>
    /// Returns true if automation was interrupted by user
    /// </summary>
    public bool WasInterrupted { get; private set; }

    /// <summary>
    /// Types a single line of text with human-like delays
    /// </summary>
    public async Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        foreach (char c in text)
        {
            if (cancellationToken.IsCancellationRequested) break;

            TypeCharacter(c);

            // Random delay for human-like typing
            int delay = _random.Next(MinTypeDelay, MaxTypeDelay);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// Types a tab-separated row (tabs between fields)
    /// </summary>
    public async Task TypeRowAsync(string[] fields, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Type the field value
            await TypeTextAsync(fields[i], cancellationToken);

            // Tab to next field (except after last field)
            if (i < fields.Length - 1)
            {
                PressTab();
                await Task.Delay(FieldDelay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Types multiple rows, pressing Enter between each
    /// </summary>
    public async Task TypeRowsAsync(
        string[][] rows,
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            progress?.Report(new TypeItProgress
            {
                CurrentRow = i + 1,
                TotalRows = rows.Length,
                CurrentDescription = rows[i].Length > 0 ? rows[i][0] : ""
            });

            await TypeRowAsync(rows[i], cancellationToken);

            // Enter to next row (except after last row)
            if (i < rows.Length - 1)
            {
                PressEnter();
                await Task.Delay(RowDelay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Types a single character using keyboard simulation
    /// </summary>
    private void TypeCharacter(char c)
    {
        // Handle Tab character
        if (c == '\t')
        {
            PressTab();
            return;
        }

        // Handle newline
        if (c == '\n' || c == '\r')
        {
            PressEnter();
            return;
        }

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
    /// Sends a key down or up event using SendInput (more reliable than keybd_event)
    /// </summary>
    private void SendKey(byte vkCode, bool down)
    {
        // Get the scan code for more reliable input
        ushort scanCode = (ushort)MapVirtualKey(vkCode, MAPVK_VK_TO_VSC);

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = scanCode,
                    dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint result = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TypeIt] SendInput failed for vkCode {vkCode}, error: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Sends a key press (down + up) using SendInput
    /// </summary>
    private void SendKeyPress(byte vkCode)
    {
        // Get the scan code for more reliable input
        ushort scanCode = (ushort)MapVirtualKey(vkCode, MAPVK_VK_TO_VSC);

        var inputs = new INPUT[2];

        // Key down
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = scanCode,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Key up
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = scanCode,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint result = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TypeIt] SendInput failed for keypress {vkCode}, error: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Sends Ctrl+V to paste from clipboard using SendInput (more reliable)
    /// </summary>
    private void SendCtrlV()
    {
        System.Diagnostics.Debug.WriteLine("[TypeIt] SendCtrlV: Starting paste sequence with SendInput...");

        // Make sure no modifier keys are pressed first
        Thread.Sleep(30);

        // Get scan codes
        ushort ctrlScan = (ushort)MapVirtualKey(VK_CONTROL, MAPVK_VK_TO_VSC);
        ushort vScan = (ushort)MapVirtualKey(VK_V, MAPVK_VK_TO_VSC);

        // Send all 4 inputs at once for atomic operation: Ctrl down, V down, V up, Ctrl up
        var inputs = new INPUT[4];

        // Ctrl down
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    wScan = ctrlScan,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // V down
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_V,
                    wScan = vScan,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // V up
        inputs[2] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_V,
                    wScan = vScan,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Ctrl up
        inputs[3] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    wScan = ctrlScan,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint result = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        System.Diagnostics.Debug.WriteLine($"[TypeIt] SendCtrlV: SendInput returned {result} (expected 4), LastError: {Marshal.GetLastWin32Error()}");

        // Small delay to let paste complete
        Thread.Sleep(50);

        System.Diagnostics.Debug.WriteLine("[TypeIt] SendCtrlV: Paste sequence complete");
    }

    /// <summary>
    /// Copy text to the Windows clipboard with retry logic and verification
    /// </summary>
    private bool CopyToClipboard(string text)
    {
        bool success = false;
        string? clipboardContent = null;

        // Try up to 3 times with increasing delays
        for (int attempt = 0; attempt < 3 && !success; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(50 * attempt); // 50ms, 100ms delays
                System.Diagnostics.Debug.WriteLine($"[TypeIt] Clipboard retry attempt {attempt + 1}");
            }

            // Use a thread to access clipboard (requires STA)
            var thread = new Thread(() =>
            {
                try
                {
                    // Clear clipboard first
                    System.Windows.Forms.Clipboard.Clear();
                    Thread.Sleep(10);

                    // Set the text
                    System.Windows.Forms.Clipboard.SetText(text, System.Windows.Forms.TextDataFormat.UnicodeText);
                    Thread.Sleep(20);

                    // Verify it was set
                    if (System.Windows.Forms.Clipboard.ContainsText())
                    {
                        clipboardContent = System.Windows.Forms.Clipboard.GetText();
                        if (clipboardContent == text)
                        {
                            success = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TypeIt] Clipboard SetText failed: {ex.Message}");
                    // Try alternate method
                    try
                    {
                        var dataObject = new System.Windows.Forms.DataObject();
                        dataObject.SetText(text, System.Windows.Forms.TextDataFormat.UnicodeText);
                        System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
                        Thread.Sleep(20);
                        success = System.Windows.Forms.Clipboard.ContainsText();
                    }
                    catch (Exception ex2)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TypeIt] Clipboard SetDataObject failed: {ex2.Message}");
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(1000); // Wait up to 1 second
        }

        System.Diagnostics.Debug.WriteLine($"[TypeIt] Clipboard copy {(success ? "SUCCESS" : "FAILED")}: {text.Length} chars, verified: {clipboardContent?.Length ?? 0} chars");
        return success;
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
    /// Switch to previous window (Alt+Tab)
    /// </summary>
    public async Task SwitchToPreviousWindowAsync()
    {
        // Press Alt+Tab to switch to previous window
        SendKey(VK_MENU, true);  // Alt down
        Thread.Sleep(20);
        SendKeyPress(VK_TAB);     // Tab press
        Thread.Sleep(20);
        SendKey(VK_MENU, false); // Alt up

        // Wait for window switch to complete
        await Task.Delay(150);
    }

    /// <summary>
    /// Right-click at current cursor position using SendInput
    /// </summary>
    public void RightClick()
    {
        var inputs = new INPUT[2];

        // Right button down
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_RIGHTDOWN,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Right button up
        inputs[1] = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_RIGHTUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint result = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TypeIt] RightClick SendInput failed, error: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Press Down arrow key
    /// </summary>
    public void PressDown()
    {
        SendKeyPress(VK_DOWN);
    }

    /// <summary>
    /// Press Up arrow key
    /// </summary>
    public void PressUp()
    {
        SendKeyPress(VK_UP);
    }

    /// <summary>
    /// Insert a new line in CCC Desktop via right-click menu
    /// Uses UI Automation to find "Insert Line" menu item regardless of position
    /// </summary>
    public async Task InsertLineInCCCAsync()
    {
        System.Diagnostics.Debug.WriteLine("[TypeIt] InsertLineInCCCAsync: Sending right-click...");
        // Right-click to open context menu
        RightClick();

        // Wait longer for menu to appear - CCC can be slow
        int menuWait = Math.Max(MenuDelay, 200);
        System.Diagnostics.Debug.WriteLine($"[TypeIt] InsertLineInCCCAsync: Waiting {menuWait}ms for menu...");
        await Task.Delay(menuWait);

        // Try UI Automation first to find "Insert Line"
        System.Diagnostics.Debug.WriteLine("[TypeIt] InsertLineInCCCAsync: Searching for 'Insert Line' menu item...");
        bool found = await FindAndClickInsertLineAsync();
        System.Diagnostics.Debug.WriteLine($"[TypeIt] InsertLineInCCCAsync: UI Automation found = {found}");

        if (!found)
        {
            // Fallback: Use keyboard navigation
            // Try different patterns since menu structure varies:
            // Pattern 1: Down, Down, Enter (Insert Line is 2nd item)
            // Pattern 2: Down, Enter (Insert Line is 1st item)
            // Pattern 3: Just press 'i' for Insert (keyboard accelerator)

            System.Diagnostics.Debug.WriteLine("[TypeIt] InsertLineInCCCAsync: Using keyboard fallback - trying 'i' key for Insert...");

            // First try pressing 'i' key (common accelerator for Insert)
            SendKeyPress(0x49); // VK_I
            await Task.Delay(100);

            // If that didn't work, try arrow key navigation
            // Check if menu is still open by trying UI Automation again
            bool stillOpen = await CheckMenuStillOpenAsync();
            if (stillOpen)
            {
                System.Diagnostics.Debug.WriteLine("[TypeIt] InsertLineInCCCAsync: Menu still open, trying Down, Down, Enter...");
                SendKeyPress(0x28); // VK_DOWN
                await Task.Delay(50);
                SendKeyPress(0x28); // VK_DOWN again
                await Task.Delay(50);
                SendKeyPress(0x0D); // VK_RETURN (Enter)
            }
        }

        // Wait for new line to be created
        await Task.Delay(Math.Max(InsertDelay, 100));
        System.Diagnostics.Debug.WriteLine("[TypeIt] InsertLineInCCCAsync: Complete");
    }

    /// <summary>
    /// Check if a context menu is still open
    /// </summary>
    private async Task<bool> CheckMenuStillOpenAsync()
    {
        try
        {
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            var menuItems = desktop.FindAllDescendants(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));

            return menuItems.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uses UI Automation to find and click the "Insert Line" menu item
    /// </summary>
    private async Task<bool> FindAndClickInsertLineAsync()
    {
        try
        {
            using var automation = new UIA3Automation();
            var desktop = automation.GetDesktop();

            // Find all menu items on screen (context menu should be visible)
            var menuItems = desktop.FindAllDescendants(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));

            foreach (var item in menuItems)
            {
                var name = item.Name?.ToLower() ?? "";

                // Look for "Insert Line" or similar
                if (name.Contains("insert") && name.Contains("line"))
                {
                    // Check if it's enabled
                    if (item.IsEnabled)
                    {
                        item.Click();
                        return true;
                    }
                }
            }

            // If we didn't find "Insert Line", look for just "Insert"
            foreach (var item in menuItems)
            {
                var name = item.Name?.ToLower() ?? "";

                if (name.Contains("insert") && !name.Contains("delete"))
                {
                    if (item.IsEnabled)
                    {
                        item.Click();
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Types multiple rows with CCC workflow:
    /// For each row: Right-click → Insert Line → Type fields across
    /// Monitors for user input - if detected, pauses and fires UserInterrupted event.
    /// </summary>
    public async Task<TypeItResult> TypeRowsWithInsertAsync(
        string[][] rows,
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await TypeRowsWithInsertAsync(rows, 0, progress, cancellationToken);
    }

    /// <summary>
    /// Types rows starting from a specific index (for resume functionality)
    /// </summary>
    public async Task<TypeItResult> TypeRowsWithInsertAsync(
        string[][] rows,
        int startFromRow,
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[TypeIt] Starting TypeRowsWithInsertAsync with {rows.Length} rows, starting from {startFromRow}");

        _currentRows = rows;
        _lastCompletedRow = startFromRow - 1;
        WasInterrupted = false;

        try
        {
            // BLOCK user input during automation (safety feature)
            // This prevents accidental clicks/typing from interfering with the sequence
            if (BlockUserInput)
            {
                System.Diagnostics.Debug.WriteLine("[TypeIt] Starting input BLOCKING (safety mode)");
                StartInputBlocking();
            }
            else if (MonitorUserInput)
            {
                // Fallback to monitoring (pauses on user input instead of blocking)
                System.Diagnostics.Debug.WriteLine("[TypeIt] Starting input monitoring");
                StartInputMonitoring();
            }

            for (int i = startFromRow; i < rows.Length; i++)
            {
                // Check for external cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    return new TypeItResult
                    {
                        Completed = false,
                        RowsCompleted = _lastCompletedRow + 1,
                        TotalRows = rows.Length,
                        WasInterrupted = false,
                        WasCancelled = true
                    };
                }

                // Check for user interrupt
                if (_userInterruptCts?.IsCancellationRequested == true)
                {
                    WasInterrupted = true;
                    return new TypeItResult
                    {
                        Completed = false,
                        RowsCompleted = _lastCompletedRow + 1,
                        TotalRows = rows.Length,
                        WasInterrupted = true,
                        WasCancelled = false
                    };
                }

                progress?.Report(new TypeItProgress
                {
                    CurrentRow = i + 1,
                    TotalRows = rows.Length,
                    CurrentDescription = rows[i].Length > 8 ? rows[i][8] : (rows[i].Length > 0 ? rows[i][0] : "")
                });

                System.Diagnostics.Debug.WriteLine($"[TypeIt] Processing row {i + 1}/{rows.Length}");

                // Build tab-separated row string
                string rowText = string.Join("\t", rows[i]);
                System.Diagnostics.Debug.WriteLine($"[TypeIt] Row text: {rowText.Substring(0, Math.Min(80, rowText.Length))}...");

                // Copy row to clipboard with verification
                System.Diagnostics.Debug.WriteLine("[TypeIt] Copying to clipboard...");
                bool clipboardSuccess = CopyToClipboard(rowText);
                if (!clipboardSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("[TypeIt] WARNING: Clipboard copy may have failed, continuing anyway...");
                }
                await Task.Delay(FieldDelay + 30); // Extra delay for clipboard sync

                // Insert a new line using UI Automation (finds "Insert Line" by name, handles greyed items)
                System.Diagnostics.Debug.WriteLine("[TypeIt] Calling InsertLineInCCCAsync...");
                await InsertLineInCCCAsync();

                // Extra delay to let the new line appear and be ready for input
                await Task.Delay(50);

                // Now cursor should be at column A of new line - paste the row
                System.Diagnostics.Debug.WriteLine("[TypeIt] Sending Ctrl+V to paste...");
                SendCtrlV();
                await Task.Delay(PasteDelay + 30); // Extra delay for paste to complete

                // Mark this row as completed
                _lastCompletedRow = i;
                System.Diagnostics.Debug.WriteLine($"[TypeIt] Row {i + 1} completed");

                // Pause between rows (configurable via RowDelay)
                await Task.Delay(RowDelay);
            }

            return new TypeItResult
            {
                Completed = true,
                RowsCompleted = rows.Length,
                TotalRows = rows.Length,
                WasInterrupted = false,
                WasCancelled = false
            };
        }
        finally
        {
            // ALWAYS restore user input, even if error occurs
            StopInputBlocking();
            StopInputMonitoring();
        }
    }

    /// <summary>
    /// Resume typing from where it was interrupted
    /// </summary>
    public async Task<TypeItResult> ResumeAsync(
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentRows == null || !CanResume)
        {
            throw new InvalidOperationException("No interrupted export to resume");
        }

        // Resume from the next row after last completed
        return await TypeRowsWithInsertAsync(_currentRows, _lastCompletedRow + 1, progress, cancellationToken);
    }

    /// <summary>
    /// Restart typing from the beginning
    /// </summary>
    public async Task<TypeItResult> RestartAsync(
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentRows == null)
        {
            throw new InvalidOperationException("No export to restart");
        }

        return await TypeRowsWithInsertAsync(_currentRows, 0, progress, cancellationToken);
    }

    /// <summary>
    /// Clear the current export state
    /// </summary>
    public void ClearState()
    {
        _currentRows = null;
        _lastCompletedRow = -1;
        WasInterrupted = false;
    }

    /// <summary>
    /// Start monitoring for user keyboard and mouse input.
    /// If real user input is detected, triggers cancellation and UserInterrupted event.
    /// </summary>
    public void StartInputMonitoring()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            _monitoringInput = true;
            _userInterruptCts = new CancellationTokenSource();

            // Create delegate instances and keep them alive
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule?.ModuleName);

            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }
    }

    /// <summary>
    /// Stop monitoring user input and remove hooks.
    /// </summary>
    public void StopInputMonitoring()
    {
        _monitoringInput = false;

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _keyboardProc = null;
        _mouseProc = null;
        _userInterruptCts = null;
    }

    /// <summary>
    /// Start BLOCKING all user input (keyboard and mouse).
    /// Uses BlockInput API if available (admin), otherwise falls back to low-level hooks.
    /// Call StopInputBlocking() when done!
    /// </summary>
    public void StartInputBlocking()
    {
        if (_blockingInput) return; // Already blocking

        System.Diagnostics.Debug.WriteLine("[TypeIt] Starting input blocking...");

        // Save current cursor position for clipping
        GetCursorPos(out POINT cursorPos);

        // Try BlockInput API first (requires admin privileges)
        try
        {
            if (BlockInput(true))
            {
                _blockInputApiAvailable = true;
                _blockingInput = true;
                System.Diagnostics.Debug.WriteLine("[TypeIt] BlockInput API succeeded - all input blocked");
                InputBlockingChanged?.Invoke(this, true);
                return;
            }
        }
        catch
        {
            // BlockInput failed (no admin), use fallback
            System.Diagnostics.Debug.WriteLine("[TypeIt] BlockInput API failed (no admin), using hooks");
        }

        // Fallback: Use low-level hooks that SWALLOW input (return 1 to block)
        System.Diagnostics.Debug.WriteLine("[TypeIt] Using hook-based input blocking");
        _blockingInput = true;
        _blockInputApiAvailable = false;

        _blockingKeyboardProc = BlockingKeyboardCallback;
        _blockingMouseProc = BlockingMouseCallback;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);

        _blockingKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _blockingKeyboardProc, moduleHandle, 0);
        _blockingMouseHook = SetWindowsHookEx(WH_MOUSE_LL, _blockingMouseProc, moduleHandle, 0);

        // Also clip cursor to current position (prevents visual mouse movement)
        // This creates a 1x1 pixel box at current position
        var clipRect = new RECT
        {
            left = cursorPos.X,
            top = cursorPos.Y,
            right = cursorPos.X + 1,
            bottom = cursorPos.Y + 1
        };
        ClipCursor(ref clipRect);

        System.Diagnostics.Debug.WriteLine($"[TypeIt] Blocking hooks installed: KB={_blockingKeyboardHook != IntPtr.Zero}, Mouse={_blockingMouseHook != IntPtr.Zero}");
        System.Diagnostics.Debug.WriteLine($"[TypeIt] Cursor clipped to ({cursorPos.X}, {cursorPos.Y})");

        InputBlockingChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Stop blocking user input and restore normal input processing.
    /// CRITICAL: Always call this in a finally block!
    /// </summary>
    public void StopInputBlocking()
    {
        if (!_blockingInput) return;

        System.Diagnostics.Debug.WriteLine("[TypeIt] Stopping input blocking...");

        if (_blockInputApiAvailable)
        {
            try
            {
                BlockInput(false);
            }
            catch
            {
                // Ignore errors
            }
        }

        // Unclip cursor (restore full movement)
        ClipCursor(IntPtr.Zero);

        // Remove blocking hooks
        if (_blockingKeyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_blockingKeyboardHook);
            _blockingKeyboardHook = IntPtr.Zero;
        }

        if (_blockingMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_blockingMouseHook);
            _blockingMouseHook = IntPtr.Zero;
        }

        _blockingKeyboardProc = null;
        _blockingMouseProc = null;
        _blockingInput = false;

        System.Diagnostics.Debug.WriteLine("[TypeIt] Input blocking stopped, cursor unclipped");
        InputBlockingChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Keyboard hook that BLOCKS (swallows) all real user input.
    /// Injected input (from our automation) passes through.
    /// </summary>
    private IntPtr BlockingKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && _blockingInput)
        {
            // Check if input is injected (programmatic)
            var flags = (uint)Marshal.ReadInt32(lParam, 8);
            bool isInjected = (flags & LLKHF_INJECTED) != 0;

            // Block real user input, allow our injected input
            if (!isInjected)
            {
                // Return 1 to BLOCK the input (don't pass to application)
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_blockingKeyboardHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Mouse hook that BLOCKS (swallows) all real user input.
    /// Injected input (from our automation) passes through.
    /// </summary>
    private IntPtr BlockingMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && _blockingInput)
        {
            // Check if input is injected (programmatic)
            var flags = (uint)Marshal.ReadInt32(lParam, 12);
            bool isInjected = (flags & LLMHF_INJECTED) != 0;

            // Block real user input, allow our injected input
            if (!isInjected)
            {
                // Return 1 to BLOCK the input (don't pass to application)
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_blockingMouseHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Trigger user interruption - cancels automation and fires event
    /// </summary>
    private void TriggerUserInterrupt()
    {
        if (_userInterruptCts != null && !_userInterruptCts.IsCancellationRequested)
        {
            _userInterruptCts.Cancel();
            UserInterrupted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Keyboard hook callback - detects real user input and triggers interrupt
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && _monitoringInput)
        {
            // Check if input is injected (programmatic)
            var flags = (uint)Marshal.ReadInt32(lParam, 8); // flags is at offset 8 in KBDLLHOOKSTRUCT
            bool isInjected = (flags & LLKHF_INJECTED) != 0;

            // If real user input detected, trigger interrupt
            if (!isInjected)
            {
                TriggerUserInterrupt();
            }
        }
        // Always pass through - we detect, not block
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Mouse hook callback - detects real user input and triggers interrupt
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && _monitoringInput)
        {
            // Check if input is injected (programmatic)
            var flags = (uint)Marshal.ReadInt32(lParam, 12); // flags is at offset 12 in MSLLHOOKSTRUCT
            bool isInjected = (flags & LLMHF_INJECTED) != 0;

            // If real user input detected, trigger interrupt
            if (!isInjected)
            {
                TriggerUserInterrupt();
            }
        }
        // Always pass through - we detect, not block
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Changes the system cursor to crosshair to indicate "data attached"
    /// </summary>
    private void SetAimingCursor()
    {
        try
        {
            // Load the crosshair cursor
            IntPtr crossCursor = LoadCursor(IntPtr.Zero, IDC_CROSS);
            if (crossCursor != IntPtr.Zero)
            {
                // Make a copy (SetSystemCursor destroys the cursor)
                IntPtr cursorCopy = CopyIcon(crossCursor);
                if (cursorCopy != IntPtr.Zero)
                {
                    // Replace the normal arrow with crosshair
                    SetSystemCursor(cursorCopy, OCR_NORMAL);
                    _cursorChanged = true;
                }
            }
        }
        catch
        {
            // Ignore cursor change errors
        }
    }

    /// <summary>
    /// Restores the system cursor to default
    /// </summary>
    private void RestoreDefaultCursor()
    {
        if (_cursorChanged)
        {
            try
            {
                // Restore all system cursors to defaults
                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                _cursorChanged = false;
            }
            catch
            {
                // Ignore restore errors
            }
        }
    }

    /// <summary>
    /// Enter aiming mode - cursor changes, waits for user to click target or press ESC
    /// </summary>
    public async Task<AimingResult> StartAimingModeAsync()
    {
        if (_inAimingMode) return new AimingResult { Cancelled = true };

        _inAimingMode = true;
        _sawMouseDown = false;  // Reset - need first click before detecting double-click
        _lastClickTime = DateTime.MinValue;  // Reset double-click timer
        _aimingTcs = new TaskCompletionSource<AimingResult>();

        // Change cursor to crosshair to show "data attached"
        SetAimingCursor();

        // Set up hooks - requires DOUBLE-CLICK to trigger paste
        // Single click just positions, double-click confirms
        StartAimingHooks();

        // Notify that aiming mode started
        AimingModeStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // Wait for user to click or press ESC
            var result = await _aimingTcs.Task;
            return result;
        }
        finally
        {
            _inAimingMode = false;
            StopAimingHooks();

            // Restore cursor to normal
            RestoreDefaultCursor();

            AimingModeEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Cancel aiming mode
    /// </summary>
    public void CancelAimingMode()
    {
        if (_inAimingMode && _aimingTcs != null)
        {
            _aimingTcs.TrySetResult(new AimingResult { Cancelled = true });
        }
    }

    private void StartAimingHooks()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardProc = AimingKeyboardCallback;
            _mouseProc = AimingMouseCallback;

            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule?.ModuleName);

            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        }
    }

    private void StopAimingHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _keyboardProc = null;
        _mouseProc = null;
    }

    /// <summary>
    /// Keyboard hook for aiming mode:
    /// - Enter or Space = CONFIRM and start typing at current cursor position
    /// - ESC = Cancel
    /// </summary>
    private IntPtr AimingKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && _inAimingMode && (uint)wParam == WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            // ESC = cancel
            if (vkCode == VK_ESCAPE)
            {
                System.Diagnostics.Debug.WriteLine("[TypeIt] ESC pressed - cancelling aiming mode");
                _aimingTcs?.TrySetResult(new AimingResult { Cancelled = true });
                return (IntPtr)1; // Block the key
            }

            // Enter or Space = confirm at current cursor position
            if (vkCode == VK_RETURN || vkCode == 0x20) // 0x20 = VK_SPACE
            {
                System.Diagnostics.Debug.WriteLine($"[TypeIt] ENTER/SPACE pressed - checking window at cursor position");
                GetCursorPos(out POINT pt);
                System.Diagnostics.Debug.WriteLine($"[TypeIt] Cursor position: {pt.X}, {pt.Y}");
                bool isCCC = IsCCCWindow(pt);
                System.Diagnostics.Debug.WriteLine($"[TypeIt] IsCCCWindow result: {isCCC}");

                _aimingTcs?.TrySetResult(new AimingResult
                {
                    Cancelled = false,
                    ClickX = pt.X,
                    ClickY = pt.Y,
                    IsCCCWindow = isCCC
                });
                return (IntPtr)1; // Block the key so it doesn't type in CCC
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Mouse hook for aiming mode - just passes through clicks so user can position cursor freely.
    /// Export is triggered by ENTER key only (not mouse click).
    /// </summary>
    private IntPtr AimingMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Don't trigger export on mouse clicks - let user click freely to position cursor
        // Only ENTER key (in AimingKeyboardCallback) triggers the export
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Check if the point is over a CCC Desktop window.
    /// Also logs debug info to help diagnose issues.
    /// </summary>
    private bool IsCCCWindow(POINT pt)
    {
        try
        {
            // Get window at click position
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[IsCCCWindow] WindowFromPoint returned null");
                return false;
            }

            // Get the root/top-level window
            IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

            // Get the process ID
            GetWindowThreadProcessId(rootHwnd, out uint processId);
            if (processId == 0)
            {
                System.Diagnostics.Debug.WriteLine("[IsCCCWindow] Could not get process ID");
                return false;
            }

            // Get process name
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            string processName = process.ProcessName.ToLower();

            // Get window title
            var titleBuilder = new System.Text.StringBuilder(256);
            GetWindowText(rootHwnd, titleBuilder, 256);
            string windowTitle = titleBuilder.ToString().ToLower();

            System.Diagnostics.Debug.WriteLine($"[IsCCCWindow] Process: '{processName}', Title: '{windowTitle}'");

            // Check for CCC Desktop - expanded patterns
            // CCC ONE, CCC Pathways, CCC Estimating, etc.
            if (processName.Contains("ccc") ||
                processName.Contains("pathways") ||
                processName.Contains("estimat") ||
                processName.Contains("one") ||
                processName.Contains("touchless"))
            {
                System.Diagnostics.Debug.WriteLine("[IsCCCWindow] MATCH by process name!");
                return true;
            }

            // Also check window title - expanded patterns
            if (windowTitle.Contains("ccc") ||
                windowTitle.Contains("pathways") ||
                windowTitle.Contains("estimate") ||
                windowTitle.Contains("collision") ||
                windowTitle.Contains("claim") ||
                windowTitle.Contains("appraisal"))
            {
                System.Diagnostics.Debug.WriteLine("[IsCCCWindow] MATCH by window title!");
                return true;
            }

            System.Diagnostics.Debug.WriteLine("[IsCCCWindow] No match - not a CCC window");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IsCCCWindow] Exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Full workflow: Enter aiming mode, wait for ENTER key, then type rows at that location
    /// SIMPLIFIED: No longer checks if it's CCC window - user knows where they're clicking
    /// </summary>
    public async Task<TypeItResult> AimAndTypeAsync(
        string[][] rows,
        IProgress<TypeItProgress>? progress = null)
    {
        System.Diagnostics.Debug.WriteLine($"[TypeIt] AimAndTypeAsync called with {rows.Length} rows");

        // Enter aiming mode and wait for ENTER or ESC
        var aimResult = await StartAimingModeAsync();

        if (aimResult.Cancelled)
        {
            System.Diagnostics.Debug.WriteLine("[TypeIt] Aiming was cancelled");
            return new TypeItResult
            {
                Completed = false,
                WasCancelled = true,
                RowsCompleted = 0,
                TotalRows = rows.Length
            };
        }

        System.Diagnostics.Debug.WriteLine($"[TypeIt] Aiming complete, proceeding to type at ({aimResult.ClickX}, {aimResult.ClickY})");

        // REMOVED IsCCCWindow check - user knows where they're clicking
        // Small delay to let focus settle
        await Task.Delay(200);

        // Use right-click → Insert Line workflow for each row
        return await TypeRowsWithInsertAsync(rows, progress);
    }

    /// <summary>
    /// Simple typing: just types each row with Tab between fields and Enter between rows
    /// No right-click menu interaction - assumes cursor is already positioned
    /// </summary>
    public async Task<TypeItResult> TypeRowsSimpleAsync(
        string[][] rows,
        IProgress<TypeItProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _currentRows = rows;
        _lastCompletedRow = -1;
        WasInterrupted = false;

        try
        {
            // BLOCK user input during automation (safety feature)
            if (BlockUserInput)
            {
                StartInputBlocking();
            }
            else if (MonitorUserInput)
            {
                StartInputMonitoring();
            }

            for (int i = 0; i < rows.Length; i++)
            {
                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    return new TypeItResult
                    {
                        Completed = false,
                        RowsCompleted = _lastCompletedRow + 1,
                        TotalRows = rows.Length,
                        WasCancelled = true
                    };
                }

                // Check for user interrupt
                if (_userInterruptCts?.IsCancellationRequested == true)
                {
                    WasInterrupted = true;
                    return new TypeItResult
                    {
                        Completed = false,
                        RowsCompleted = _lastCompletedRow + 1,
                        TotalRows = rows.Length,
                        WasInterrupted = true
                    };
                }

                progress?.Report(new TypeItProgress
                {
                    CurrentRow = i + 1,
                    TotalRows = rows.Length,
                    CurrentDescription = rows[i].Length > 1 ? rows[i][1] : (rows[i].Length > 0 ? rows[i][0] : "")
                });

                // Type this row - fields separated by Tab
                await TypeRowAsync(rows[i], cancellationToken);

                _lastCompletedRow = i;

                // Enter to go to next row (except after last row)
                if (i < rows.Length - 1)
                {
                    PressEnter();
                    await Task.Delay(RowDelay, cancellationToken);
                }
            }

            return new TypeItResult
            {
                Completed = true,
                RowsCompleted = rows.Length,
                TotalRows = rows.Length
            };
        }
        finally
        {
            StopInputBlocking();
            StopInputMonitoring();
        }
    }

    public void Dispose()
    {
        // Ensure cursor is restored if we exit during aiming
        RestoreDefaultCursor();

        // CRITICAL: Ensure input blocking is stopped
        StopInputBlocking();

        // Ensure monitoring is stopped when disposing
        StopInputMonitoring();
        StopAimingHooks();
        _disposed = true;
    }
}

/// <summary>
/// Result of aiming mode
/// </summary>
public class AimingResult
{
    public bool Cancelled { get; set; }
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public bool IsCCCWindow { get; set; }
}

/// <summary>
/// Progress information during typing
/// </summary>
public class TypeItProgress
{
    public int CurrentRow { get; set; }
    public int TotalRows { get; set; }
    public string CurrentDescription { get; set; } = "";
}

/// <summary>
/// Result of a typing operation
/// </summary>
public class TypeItResult
{
    /// <summary>
    /// True if all rows were typed successfully
    /// </summary>
    public bool Completed { get; set; }

    /// <summary>
    /// Number of rows that were completed
    /// </summary>
    public int RowsCompleted { get; set; }

    /// <summary>
    /// Total rows in the operation
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// True if operation was stopped due to user input
    /// </summary>
    public bool WasInterrupted { get; set; }

    /// <summary>
    /// True if operation was cancelled via cancellation token
    /// </summary>
    public bool WasCancelled { get; set; }

    /// <summary>
    /// True if user clicked outside of CCC Desktop window (safety rejection)
    /// </summary>
    public bool NotCCCWindow { get; set; }

    /// <summary>
    /// Number of rows remaining
    /// </summary>
    public int RowsRemaining => TotalRows - RowsCompleted;
}
