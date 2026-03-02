#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace McStudDesktop.Services;

/// <summary>
/// Paste automation service for McStud Desktop.
/// User positions cursor in target app, switches to McStud, clicks button.
/// We Alt+Tab back, detect interrupts, type fast, pause if user interferes.
/// Supports resume from where it stopped.
/// </summary>
public class AutoHotkeyPasteService : IDisposable
{
    #region Win32 APIs

    [DllImport("user32.dll")]
    private static extern bool BlockInput(bool fBlockIt);

    // Low-level hooks for interrupt detection
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    // Flag to detect injected input (our own SendInput)
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    // Clipboard APIs
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // INPUT struct - size must match Windows definition (includes all union members)
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // Union must include all members so size is computed correctly
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    // MOUSEINPUT - largest union member, determines INPUT size
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
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
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
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const byte VK_TAB = 0x09;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_V = 0x56;

    #endregion

    // Speed settings - like SetKeyDelay in AutoHotkey
    public int KeyDelay { get; set; } = 20;      // Delay after pasting text
    public int TabDelay { get; set; } = 30;      // Delay after Tab
    public int EnterDelay { get; set; } = 50;    // Delay after Enter

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<(int current, int total)>? ProgressChanged;
    public event EventHandler? Interrupted;  // Fired when user input detected

    private bool _isRunning = false;
    private bool _cancelled = false;
    private bool _interrupted = false;

    // Resume state
    private string[][]? _savedRows;
    private int _resumeRowIndex = 0;
    private int _resumeFieldIndex = 0;
    public bool CanResume => _savedRows != null && _resumeRowIndex < _savedRows.Length;

    // Hook handles
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private LowLevelProc? _keyboardProc;
    private LowLevelProc? _mouseProc;
    private bool _hooksInstalled = false;

    /// <summary>
    /// Paste rows - AutoHotkey style with interrupt detection.
    /// 1. Alt+Tab back to where user was (they positioned cursor there)
    /// 2. Install hooks to detect user input
    /// 3. Paste each field with Tab, Enter for new rows
    /// 4. If interrupted, save position for resume
    /// </summary>
    public async Task<bool> PasteToApp(string[][] rows, bool switchWindow = true)
    {
        if (_isRunning) return false;
        _isRunning = true;
        _cancelled = false;
        _interrupted = false;

        // Save rows for potential resume
        _savedRows = rows;
        _resumeRowIndex = 0;
        _resumeFieldIndex = 0;

        return await DoPaste(rows, 0, 0, switchWindow);
    }

    /// <summary>
    /// Resume from where we left off after an interrupt
    /// </summary>
    public async Task<bool> Resume(bool switchWindow = true)
    {
        if (_isRunning) return false;
        if (_savedRows == null || _resumeRowIndex >= _savedRows.Length)
        {
            StatusChanged?.Invoke(this, "Nothing to resume");
            return false;
        }

        _isRunning = true;
        _cancelled = false;
        _interrupted = false;

        return await DoPaste(_savedRows, _resumeRowIndex, _resumeFieldIndex, switchWindow);
    }

    /// <summary>
    /// Clear saved state (call when starting fresh)
    /// </summary>
    public void ClearState()
    {
        _savedRows = null;
        _resumeRowIndex = 0;
        _resumeFieldIndex = 0;
    }

    private async Task<bool> DoPaste(string[][] rows, int startRow, int startField, bool switchWindow)
    {
        bool success = false;

        try
        {
            int remaining = rows.Length - startRow;
            StatusChanged?.Invoke(this, startRow > 0
                ? $"Resuming: {remaining} rows left"
                : $"Starting: {rows.Length} rows");

            // Alt+Tab back to previous window (where user positioned cursor)
            if (switchWindow)
            {
                StatusChanged?.Invoke(this, "Switching back...");
                SendAltTab();
                await Task.Delay(400); // Let window activate
            }

            // Install hooks to detect user interference
            InstallHooks();
            StatusChanged?.Invoke(this, "Pasting... (click/key to pause)");

            await Task.Delay(100); // Small settle time

            // Paste each row
            for (int i = startRow; i < rows.Length && !_cancelled && !_interrupted; i++)
            {
                var row = rows[i];
                ProgressChanged?.Invoke(this, (i + 1, rows.Length));

                // Determine starting field (only matters for first row on resume)
                int fieldStart = (i == startRow) ? startField : 0;

                // Type each field, Tab between
                for (int j = fieldStart; j < row.Length && !_cancelled && !_interrupted; j++)
                {
                    var field = row[j];

                    if (!string.IsNullOrEmpty(field) && field != "0")
                    {
                        TypeText(field);
                        await Task.Delay(KeyDelay);
                    }

                    // Tab to next field
                    SendKey(VK_TAB);
                    await Task.Delay(TabDelay);

                    // Save position after each field
                    _resumeRowIndex = i;
                    _resumeFieldIndex = j + 1;
                }

                // Enter for next row (only if we completed all fields)
                if (!_interrupted && !_cancelled)
                {
                    SendKey(VK_RETURN);
                    await Task.Delay(EnterDelay);

                    // Move to next row
                    _resumeRowIndex = i + 1;
                    _resumeFieldIndex = 0;
                }
            }

            if (_interrupted)
            {
                int rowsLeft = rows.Length - _resumeRowIndex;
                StatusChanged?.Invoke(this, $"PAUSED at row {_resumeRowIndex + 1} - {rowsLeft} rows left. Click Resume to continue.");
                Interrupted?.Invoke(this, EventArgs.Empty);
            }
            else if (_cancelled)
            {
                StatusChanged?.Invoke(this, "Cancelled");
            }
            else
            {
                StatusChanged?.Invoke(this, $"Done! {rows.Length} rows");
                _savedRows = null; // Clear state on success
                success = true;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
        }
        finally
        {
            UninstallHooks();
            _isRunning = false;
        }

        return success;
    }

    #region Hook Management

    private void InstallHooks()
    {
        if (_hooksInstalled) return;

        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);

        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);

        _hooksInstalled = true;
    }

    private void UninstallHooks()
    {
        if (!_hooksInstalled) return;

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

        _hooksInstalled = false;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRunning && !_interrupted)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                // Check if this is injected input (our own SendInput) - ignore it
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((hookStruct.flags & LLKHF_INJECTED) == 0)
                {
                    // Real user pressed a key - interrupt!
                    _interrupted = true;
                }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRunning && !_interrupted)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                // Check if this is injected input - ignore it
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((hookStruct.flags & LLMHF_INJECTED) == 0)
                {
                    // Real user clicked - interrupt!
                    _interrupted = true;
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    #endregion

    /// <summary>
    /// Cancel the current paste operation
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
    }

    /// <summary>
    /// Type text using SendInput with KEYEVENTF_UNICODE - exactly like AutoHotkey.
    /// Each character is sent directly as a Unicode scancode, no clipboard needed.
    /// </summary>
    private void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // AutoHotkey style: send each character as Unicode via SendInput
        // 2 events per character (down + up)
        var inputs = new INPUT[text.Length * 2];

        for (int i = 0; i < text.Length; i++)
        {
            ushort c = text[i];

            // Key down - Unicode
            inputs[i * 2].type = INPUT_KEYBOARD;
            inputs[i * 2].u.ki.wVk = 0; // Must be 0 for Unicode
            inputs[i * 2].u.ki.wScan = c;
            inputs[i * 2].u.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[i * 2].u.ki.time = 0;
            inputs[i * 2].u.ki.dwExtraInfo = IntPtr.Zero;

            // Key up - Unicode
            inputs[i * 2 + 1].type = INPUT_KEYBOARD;
            inputs[i * 2 + 1].u.ki.wVk = 0;
            inputs[i * 2 + 1].u.ki.wScan = c;
            inputs[i * 2 + 1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            inputs[i * 2 + 1].u.ki.time = 0;
            inputs[i * 2 + 1].u.ki.dwExtraInfo = IntPtr.Zero;
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Set clipboard text using Win32 APIs directly.
    /// </summary>
    private void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;

        try
        {
            EmptyClipboard();

            // Allocate global memory for the text (Unicode)
            int bytes = (text.Length + 1) * 2; // Unicode = 2 bytes per char
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return;

            IntPtr target = GlobalLock(hGlobal);
            if (target != IntPtr.Zero)
            {
                // Copy the string to global memory
                for (int i = 0; i < text.Length; i++)
                {
                    Marshal.WriteInt16(target, i * 2, text[i]);
                }
                Marshal.WriteInt16(target, text.Length * 2, 0); // Null terminator
                GlobalUnlock(hGlobal);

                SetClipboardData(CF_UNICODETEXT, hGlobal);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Send a single key using SendInput
    /// </summary>
    private void SendKey(byte vk)
    {
        var inputs = new INPUT[2];

        // Key down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vk;
        inputs[0].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        inputs[0].u.ki.dwFlags = 0;
        inputs[0].u.ki.time = 0;
        inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

        // Key up
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = vk;
        inputs[1].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
        inputs[1].u.ki.time = 0;
        inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Alt+Tab to switch to previous window
    /// </summary>
    private void SendAltTab()
    {
        var inputs = new INPUT[4];

        // Alt down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_MENU;
        inputs[0].u.ki.dwFlags = 0;

        // Tab down
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_TAB;
        inputs[1].u.ki.dwFlags = 0;

        // Tab up
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_TAB;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

        // Alt up
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_MENU;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        UninstallHooks();
        BlockInput(false); // Safety: always unblock (in case it was working)
    }
}
