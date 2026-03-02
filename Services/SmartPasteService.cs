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
    /// Smart Paste Service - Enhanced paste automation for McStud Desktop
    ///
    /// Key features:
    /// 1. Sends keystrokes DIRECTLY to CCC window (not to focused window)
    /// 2. User CAN use mouse/keyboard during paste - won't interfere
    /// 3. Fast and reliable native implementation
    /// </summary>
    public class SmartPasteService
    {
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
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // PostMessage - sends message to window's message queue (async, non-blocking)
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // SendMessage - sends message and waits for processing (sync)
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Get the focused control within a window
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        // Set focus to a control
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        // Get the thread's focused window
        [DllImport("user32.dll")]
        private static extern IntPtr GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        // Window messages
        private const uint WM_CHAR = 0x0102;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_SETTEXT = 0x000C;

        private const int SW_RESTORE = 9;
        private const byte VK_TAB = 0x09;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_ALT = 0x12;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
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

        #endregion

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<PasteProgressArgs>? ProgressChanged;
        public event EventHandler<bool>? PasteCompleted;

        // State
        private IntPtr _targetWindow = IntPtr.Zero;
        private string _targetWindowTitle = "";
        private bool _isPasting = false;
        private CancellationTokenSource? _cts;

        // Speed settings (adjustable)
        private int _tabDelay = 15;      // Delay after each Tab (ms)
        private int _enterDelay = 50;    // Delay after each Enter (ms)
        private int _initialDelay = 200; // Delay after window activation (ms)

        public bool IsPasting => _isPasting;

        /// <summary>
        /// Set paste speed delays
        /// </summary>
        public void SetDelays(int tabDelayMs, int enterDelayMs, int initialDelayMs)
        {
            _tabDelay = Math.Max(5, tabDelayMs);      // Minimum 5ms
            _enterDelay = Math.Max(10, enterDelayMs); // Minimum 10ms
            _initialDelay = Math.Max(100, initialDelayMs);
            System.Diagnostics.Debug.WriteLine($"[SmartPaste] Delays set: tab={_tabDelay}ms, enter={_enterDelay}ms, initial={_initialDelay}ms");
        }

        /// <summary>
        /// Capture the currently focused window as the paste target
        /// Call this RIGHT BEFORE starting paste - captures where user clicked
        /// </summary>
        public void CaptureTargetWindow()
        {
            _targetWindow = GetForegroundWindow();
            _targetWindowTitle = GetWindowTitle(_targetWindow);
            StatusChanged?.Invoke(this, $"Target locked: {_targetWindowTitle}");
        }

        /// <summary>
        /// Set a specific window as target by handle
        /// </summary>
        public void SetTargetWindow(IntPtr hwnd)
        {
            _targetWindow = hwnd;
            _targetWindowTitle = GetWindowTitle(hwnd);
            StatusChanged?.Invoke(this, $"Target set: {_targetWindowTitle}");
        }

        /// <summary>
        /// Get window title from handle
        /// </summary>
        private string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// Force focus to target window (works even if user clicked elsewhere)
        /// </summary>
        private bool FocusTargetWindow()
        {
            if (_targetWindow == IntPtr.Zero) return false;
            return ForceActivateWindow(_targetWindow);
        }

        /// <summary>
        /// Force window activation using ALT key trick.
        /// This bypasses Windows restrictions on SetForegroundWindow.
        /// </summary>
        private bool ForceActivateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                // Get threads
                var currentForeground = GetForegroundWindow();
                var foregroundThread = GetWindowThreadProcessId(currentForeground, out _);
                var targetThread = GetWindowThreadProcessId(hwnd, out _);
                var ourThread = GetCurrentThreadId();

                // Attach to foreground thread
                if (ourThread != foregroundThread)
                {
                    AttachThreadInput(ourThread, foregroundThread, true);
                }

                // Press and release ALT key - this "unlocks" SetForegroundWindow
                // Same trick AutoHotkey uses
                keybd_event(VK_ALT, 0, 0, UIntPtr.Zero);
                keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Now we can set foreground window
                ShowWindow(hwnd, SW_RESTORE);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);

                // Detach
                if (ourThread != foregroundThread)
                {
                    AttachThreadInput(ourThread, foregroundThread, false);
                }

                System.Threading.Thread.Sleep(50);
                return GetForegroundWindow() == hwnd;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// SMART PASTE - Improved over AutoHotkey
        /// Forces focus to CCC at start of each row, types row quickly
        /// </summary>
        public async Task PasteToTargetAsync(List<List<string>> rows, CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartPaste] Starting paste of {rows.Count} rows");

            if (rows.Count == 0)
            {
                StatusChanged?.Invoke(this, "No data to paste");
                return;
            }

            if (_targetWindow == IntPtr.Zero)
            {
                StatusChanged?.Invoke(this, "No target window set. Click in CCC first.");
                return;
            }

            _isPasting = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                StatusChanged?.Invoke(this, $"Pasting {rows.Count} rows to {_targetWindowTitle}...");

                int totalRows = rows.Count;

                // Activate window ONCE at the beginning, then type all rows
                bool activated = ForceActivateWindow(_targetWindow);
                System.Diagnostics.Debug.WriteLine($"[SmartPaste] Window activation: {activated}");
                await Task.Delay(_initialDelay, _cts.Token); // Give window time to fully activate

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var row = rows[rowIndex];

                    // Type this entire row quickly
                    await TypeRowAsync(row, _cts.Token);

                    // Press Enter to move to next row
                    SendKeyPress(VK_RETURN);
                    await Task.Delay(_enterDelay, _cts.Token);

                    // NO per-row progress updates - they slow down pasting
                }

                System.Diagnostics.Debug.WriteLine($"[SmartPaste] Completed all {totalRows} rows");
                StatusChanged?.Invoke(this, $"Done! Pasted {totalRows} rows.");
                PasteCompleted?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Paste cancelled");
                PasteCompleted?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                PasteCompleted?.Invoke(this, false);
            }
            finally
            {
                _isPasting = false;
            }
        }

        /// <summary>
        /// Type an entire row quickly - field, tab, field, tab, etc.
        /// Types ALL values including zeros to maintain data alignment
        /// </summary>
        private async Task TypeRowAsync(List<string> fields, CancellationToken ct)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var value = fields[i];

                // Type ALL values (including 0) to maintain alignment
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

        /// <summary>
        /// Type text using SendInput (Unicode)
        /// </summary>
        private void TypeText(string text)
        {
            foreach (char c in text)
            {
                var inputs = new INPUT[2];

                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].ki.wVk = 0;
                inputs[0].ki.wScan = (ushort)c;
                inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[0].ki.time = 0;
                inputs[0].ki.dwExtraInfo = UIntPtr.Zero;

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].ki.wVk = 0;
                inputs[1].ki.wScan = (ushort)c;
                inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                inputs[1].ki.time = 0;
                inputs[1].ki.dwExtraInfo = UIntPtr.Zero;

                SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            }
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
        /// Cancel ongoing paste
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
        }
    }

    public class PasteProgressArgs : EventArgs
    {
        public int Current { get; }
        public int Total { get; }
        public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;

        public PasteProgressArgs(int current, int total)
        {
            Current = current;
            Total = total;
        }
    }
}
