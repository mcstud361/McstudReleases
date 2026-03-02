#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Global keyboard hook to capture Ctrl+Alt+V anywhere
    /// This allows seamless export: copy from Excel, click into CCC, press Ctrl+Alt+V
    /// </summary>
    public class GlobalHotkeyService : IDisposable
    {
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_V = 0x56;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // ALT key

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _isExporting = false;

        public event Action? OnExportHotkeyPressed;

        public GlobalHotkeyService()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookId == IntPtr.Zero)
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                if (curModule != null)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc!, GetModuleHandle(curModule.ModuleName), 0);
                    System.Diagnostics.Debug.WriteLine($"Keyboard hook installed: {_hookId != IntPtr.Zero}");
                }
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Set this to true while exporting to prevent re-triggering
        /// </summary>
        public bool IsExporting
        {
            get => _isExporting;
            set => _isExporting = value;
        }

        private bool IsKeyDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Check for Ctrl+Alt+V
                if (vkCode == VK_V && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU) && !_isExporting)
                {
                    System.Diagnostics.Debug.WriteLine("Ctrl+Alt+V pressed - triggering export");
                    OnExportHotkeyPressed?.Invoke();
                    // Return non-zero to block the keypress from reaching other apps
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
