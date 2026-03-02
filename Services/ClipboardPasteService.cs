#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Paste automation service for McStud Desktop.
    /// Reads clipboard data (tab-separated rows) and simulates typing into CCC ONE.
    /// Uses SendInput for reliable keyboard simulation directly from the app.
    /// </summary>
    public class ClipboardPasteService
    {
        // Win32 API imports for sending keystrokes
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        // BlockInput API - blocks ALL keyboard and mouse input (requires admin, or we use fallback)
        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        // Low-level hooks for blocking fallback
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
        private const uint LLKHF_INJECTED = 0x10;
        private const uint LLMHF_INJECTED = 0x01;

        // Virtual key codes
        private const byte VK_TAB = 0x09;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU = 0x12; // Alt key

        // Key event flags
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // Configurable delays (in milliseconds) - OPTIMIZED for speed
        public int DelayBetweenKeys { get; set; } = 3;   // Was 30 - now ultra-fast
        public int DelayAfterTab { get; set; } = 8;      // Was 50 - now fast
        public int DelayAfterEnter { get; set; } = 15;   // Was 150 - now fast
        public int DelayBeforeStart { get; set; } = 200; // Was 500 - reduced

        /// <summary>
        /// Block user input during paste (prevents accidental interference)
        /// </summary>
        public bool BlockUserInput { get; set; } = true;

        // Events
        public event EventHandler<PasteProgressEventArgs>? ProgressChanged;
        public event EventHandler<PasteCompletedEventArgs>? PasteCompleted;
        public event EventHandler<string>? StatusChanged;

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isPasting = false;

        // Input blocking state
        private bool _blockingInput = false;
        private bool _blockInputApiAvailable = false;
        private IntPtr _blockingKeyboardHook = IntPtr.Zero;
        private IntPtr _blockingMouseHook = IntPtr.Zero;
        private LowLevelProc? _blockingKeyboardProc;
        private LowLevelProc? _blockingMouseProc;

        public bool IsPasting => _isPasting;

        /// <summary>
        /// Set speed preset (0=Slow, 1=Normal, 2=Fast, 3=Turbo, 4=Insane)
        /// </summary>
        public void SetSpeedLevel(int speedLevel)
        {
            switch (speedLevel)
            {
                case 0: // Slow
                    DelayBetweenKeys = 30; DelayAfterTab = 50; DelayAfterEnter = 150; DelayBeforeStart = 500;
                    break;
                case 1: // Normal
                    DelayBetweenKeys = 15; DelayAfterTab = 30; DelayAfterEnter = 80; DelayBeforeStart = 350;
                    break;
                case 2: // Fast
                    DelayBetweenKeys = 8; DelayAfterTab = 15; DelayAfterEnter = 40; DelayBeforeStart = 250;
                    break;
                case 3: // Turbo
                    DelayBetweenKeys = 5; DelayAfterTab = 10; DelayAfterEnter = 20; DelayBeforeStart = 200;
                    break;
                case 4: // Insane
                    DelayBetweenKeys = 2; DelayAfterTab = 5; DelayAfterEnter = 10; DelayBeforeStart = 150;
                    break;
                default:
                    SetSpeedLevel(4); // Default to Insane
                    break;
            }
        }

        /// <summary>
        /// Read clipboard and return parsed rows/columns
        /// </summary>
        public async Task<List<List<string>>> GetClipboardDataAsync()
        {
            var rows = new List<List<string>>();

            try
            {
                var dataPackage = Clipboard.GetContent();
                if (dataPackage == null)
                    return rows;

                if (dataPackage.Contains(StandardDataFormats.Text))
                {
                    var text = await dataPackage.GetTextAsync();
                    rows = ParseClipboardText(text);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardPaste] Error reading clipboard: {ex.Message}");
            }

            return rows;
        }

        /// <summary>
        /// Parse clipboard text into rows and columns (tab-separated)
        /// </summary>
        public List<List<string>> ParseClipboardText(string text)
        {
            var rows = new List<List<string>>();

            if (string.IsNullOrWhiteSpace(text))
                return rows;

            // Split by newlines (handle different line endings)
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Split by tabs (columns)
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
        /// Start paste automation (replicates AutoHotkey Ctrl+Alt+V behavior)
        /// Now with INPUT BLOCKING for safety - user cannot interfere!
        /// </summary>
        public async Task StartPasteAsync(string? textToPaste = null)
        {
            if (_isPasting)
            {
                StatusChanged?.Invoke(this, "Already pasting. Please wait or cancel.");
                return;
            }

            _isPasting = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                // Get data from clipboard or use provided text
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
                    StatusChanged?.Invoke(this, "Clipboard is empty or contains no valid data.");
                    return;
                }

                StatusChanged?.Invoke(this, $"Starting paste: {rows.Count} rows. Input BLOCKED during paste...");

                // Give user time to switch to target window BEFORE we block input
                await Task.Delay(DelayBeforeStart, token);

                // BLOCK USER INPUT - prevent accidental interference
                if (BlockUserInput)
                {
                    StartInputBlocking();
                    System.Diagnostics.Debug.WriteLine("[ClipboardPaste] Input blocked - starting fast paste");
                }

                int totalRows = rows.Count;
                int currentRow = 0;

                foreach (var row in rows)
                {
                    if (token.IsCancellationRequested)
                    {
                        StatusChanged?.Invoke(this, "Paste cancelled by user.");
                        break;
                    }

                    currentRow++;
                    // Don't update UI during blocking (slows things down)
                    if (currentRow % 10 == 0 || currentRow == totalRows)
                    {
                        StatusChanged?.Invoke(this, $"Pasting row {currentRow} of {totalRows}...");
                    }

                    // Type each field and tab to next
                    for (int i = 0; i < row.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        var field = row[i];

                        // Type the field value if not empty
                        if (!string.IsNullOrEmpty(field))
                        {
                            await TypeTextAsync(field, token);
                        }

                        // Tab to next field
                        await Task.Delay(DelayBetweenKeys, token);
                        SendKey(VK_TAB);
                        await Task.Delay(DelayAfterTab, token);
                    }

                    // Press Enter to move to next row
                    SendKey(VK_RETURN);
                    await Task.Delay(DelayAfterEnter, token);

                    // Report progress
                    ProgressChanged?.Invoke(this, new PasteProgressEventArgs(currentRow, totalRows));
                }

                PasteCompleted?.Invoke(this, new PasteCompletedEventArgs(currentRow, !token.IsCancellationRequested));
                StatusChanged?.Invoke(this, $"Paste complete! {currentRow} rows processed.");
            }
            catch (TaskCanceledException)
            {
                StatusChanged?.Invoke(this, "Paste cancelled.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during paste: {ex.Message}");
            }
            finally
            {
                // CRITICAL: Always restore user input
                StopInputBlocking();
                _isPasting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Cancel ongoing paste operation
        /// </summary>
        public void CancelPaste()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Type text character by character using SendInput
        /// </summary>
        private async Task TypeTextAsync(string text, CancellationToken token)
        {
            foreach (char c in text)
            {
                if (token.IsCancellationRequested) break;

                SendCharacter(c);
                await Task.Delay(DelayBetweenKeys, token);
            }
        }

        /// <summary>
        /// Send a single character using keyboard simulation
        /// </summary>
        private void SendCharacter(char c)
        {
            // Use Unicode input for reliable character sending
            var inputs = new INPUT[2];

            // Key down
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = 0;
            inputs[0].ki.wScan = (ushort)c;
            inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[0].ki.time = 0;
            inputs[0].ki.dwExtraInfo = UIntPtr.Zero;

            // Key up
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = 0;
            inputs[1].ki.wScan = (ushort)c;
            inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            inputs[1].ki.time = 0;
            inputs[1].ki.dwExtraInfo = UIntPtr.Zero;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Send a virtual key (Tab, Enter, etc.)
        /// </summary>
        private void SendKey(byte vk)
        {
            var inputs = new INPUT[2];

            // Key down
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = vk;
            inputs[0].ki.wScan = (ushort)MapVirtualKey(vk, 0);
            inputs[0].ki.dwFlags = 0;
            inputs[0].ki.time = 0;
            inputs[0].ki.dwExtraInfo = UIntPtr.Zero;

            // Key up
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = vk;
            inputs[1].ki.wScan = (ushort)MapVirtualKey(vk, 0);
            inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[1].ki.time = 0;
            inputs[1].ki.dwExtraInfo = UIntPtr.Zero;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Get the title of the currently focused window
        /// </summary>
        public string GetForegroundWindowTitle()
        {
            var hwnd = GetForegroundWindow();
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            return sb.ToString();
        }

        #region Input Blocking

        /// <summary>
        /// Start BLOCKING all user input (keyboard and mouse).
        /// Uses BlockInput API if available (admin), otherwise falls back to low-level hooks.
        /// </summary>
        private void StartInputBlocking()
        {
            if (_blockingInput) return;

            System.Diagnostics.Debug.WriteLine("[ClipboardPaste] Starting input blocking...");

            // Try BlockInput API first (requires admin privileges)
            try
            {
                if (BlockInput(true))
                {
                    _blockInputApiAvailable = true;
                    _blockingInput = true;
                    System.Diagnostics.Debug.WriteLine("[ClipboardPaste] BlockInput API succeeded");
                    return;
                }
            }
            catch
            {
                // BlockInput failed (no admin), use fallback
            }

            // Fallback: Use low-level hooks that SWALLOW input
            System.Diagnostics.Debug.WriteLine("[ClipboardPaste] Using hook-based input blocking");
            _blockingInput = true;
            _blockInputApiAvailable = false;

            _blockingKeyboardProc = BlockingKeyboardCallback;
            _blockingMouseProc = BlockingMouseCallback;

            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule?.ModuleName);

            _blockingKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _blockingKeyboardProc, moduleHandle, 0);
            _blockingMouseHook = SetWindowsHookEx(WH_MOUSE_LL, _blockingMouseProc, moduleHandle, 0);
        }

        /// <summary>
        /// Stop blocking user input and restore normal input processing.
        /// </summary>
        private void StopInputBlocking()
        {
            if (!_blockingInput) return;

            System.Diagnostics.Debug.WriteLine("[ClipboardPaste] Stopping input blocking...");

            if (_blockInputApiAvailable)
            {
                try { BlockInput(false); } catch { }
            }

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
        }

        /// <summary>
        /// Keyboard hook that BLOCKS all real user input.
        /// </summary>
        private IntPtr BlockingKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= HC_ACTION && _blockingInput)
            {
                var flags = (uint)Marshal.ReadInt32(lParam, 8);
                bool isInjected = (flags & LLKHF_INJECTED) != 0;
                if (!isInjected) return (IntPtr)1; // Block real input
            }
            return CallNextHookEx(_blockingKeyboardHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Mouse hook that BLOCKS all real user input.
        /// </summary>
        private IntPtr BlockingMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= HC_ACTION && _blockingInput)
            {
                var flags = (uint)Marshal.ReadInt32(lParam, 12);
                bool isInjected = (flags & LLMHF_INJECTED) != 0;
                if (!isInjected) return (IntPtr)1; // Block real input
            }
            return CallNextHookEx(_blockingMouseHook, nCode, wParam, lParam);
        }

        #endregion

        #region SendInput Structures

        private const int INPUT_KEYBOARD = 1;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        #endregion
    }

    #region Event Args

    public class PasteProgressEventArgs : EventArgs
    {
        public int CurrentRow { get; }
        public int TotalRows { get; }
        public double PercentComplete => TotalRows > 0 ? (double)CurrentRow / TotalRows * 100 : 0;

        public PasteProgressEventArgs(int currentRow, int totalRows)
        {
            CurrentRow = currentRow;
            TotalRows = totalRows;
        }
    }

    public class PasteCompletedEventArgs : EventArgs
    {
        public int RowsProcessed { get; }
        public bool Success { get; }

        public PasteCompletedEventArgs(int rowsProcessed, bool success)
        {
            RowsProcessed = rowsProcessed;
            Success = success;
        }
    }

    #endregion
}
