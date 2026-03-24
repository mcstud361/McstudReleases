#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using McstudDesktop.Models;
using McstudDesktop.Services;

namespace McStudDesktop.Services
{
    /// <summary>
    /// CCC Desktop adapter — delegates to existing FlaUI-based automation services.
    /// SupportsElementDiscovery = true (can find and click UI elements by text).
    /// </summary>
    public class CccDesktopAdapter : IEstimatingSystemAdapter, IDisposable
    {
        private readonly TypeItService _typeItService;
        private readonly CCCAutomationService _cccAutomation;
        private bool _connected;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        private const int SW_RESTORE = 9;

        public string SystemName => "CCC Desktop";
        public bool SupportsElementDiscovery => true;
        public bool IsConnected => _connected && _cccAutomation.IsConnected;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<AutomationProgress>? ProgressChanged;

        public CccDesktopAdapter()
        {
            _typeItService = new TypeItService();
            _typeItService.SetSpeedLevel(3); // Turbo for automation
            _cccAutomation = new CCCAutomationService();
        }

        public bool CanHandle(OcrEstimateSource source) => source == OcrEstimateSource.CCCOne;

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                _connected = _cccAutomation.ConnectToCCC();
                if (_connected)
                    StatusChanged?.Invoke(this, "Connected to CCC Desktop");
                else
                    StatusChanged?.Invoke(this, "CCC Desktop not found");
                return Task.FromResult(_connected);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] Connect error: {ex.Message}");
                StatusChanged?.Invoke(this, $"Connection error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> FocusEstimatingWindowAsync(CancellationToken ct = default)
        {
            try
            {
                var window = _cccAutomation.GetTargetWindow();
                if (window != null)
                {
                    window.SetForeground();
                    return Task.FromResult(true);
                }

                // Fallback: find via ScreenCaptureService
                var windows = ScreenCaptureService.Instance.FindEstimatingWindows();
                foreach (var (hWnd, title) in windows)
                {
                    if (title.Contains("CCC", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                        return Task.FromResult(true);
                    }
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] Focus error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> InsertNewLineAsync(CancellationToken ct = default)
        {
            try
            {
                await _typeItService.InsertLineInCCCAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] InsertLine error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TypeInFieldAsync(string value, CancellationToken ct = default)
        {
            try
            {
                await _typeItService.TypeTextAsync(value, ct);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] TypeInField error: {ex.Message}");
                return false;
            }
        }

        public Task<bool> TabToNextFieldAsync(CancellationToken ct = default)
        {
            try
            {
                _typeItService.PressTab();
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] Tab error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> PressEnterAsync(CancellationToken ct = default)
        {
            try
            {
                _typeItService.PressEnter();
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] Enter error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> PressEscapeAsync(CancellationToken ct = default)
        {
            return PressKeyAsync("Escape", ct);
        }

        public Task<bool> PressKeyAsync(string key, CancellationToken ct = default)
        {
            try
            {
                // Use SendInput for arbitrary keys
                var vk = key.ToUpperInvariant() switch
                {
                    "ESCAPE" or "ESC" => (byte)0x1B,
                    "TAB" => (byte)0x09,
                    "ENTER" or "RETURN" => (byte)0x0D,
                    "UP" => (byte)0x26,
                    "DOWN" => (byte)0x28,
                    "LEFT" => (byte)0x25,
                    "RIGHT" => (byte)0x27,
                    "DELETE" or "DEL" => (byte)0x2E,
                    "BACKSPACE" => (byte)0x08,
                    _ => (byte)0
                };

                if (vk == 0) return Task.FromResult(false);

                KeyPress(vk);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] PressKey error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> ClickElementAsync(string elementText, CancellationToken ct = default)
        {
            try
            {
                using var uiService = new UIAutomationService();
                var window = _cccAutomation.GetTargetWindow();
                if (window == null) return Task.FromResult(false);

                return Task.FromResult(uiService.ClickButtonByName(window, elementText));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] ClickElement error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<ScreenOcrResult?> ReadCurrentScreenAsync(CancellationToken ct = default)
        {
            try
            {
                var (bitmap, windowTitle) = ScreenCaptureService.Instance.CaptureEstimatingWindow();
                if (bitmap == null) return null;

                var result = await ScreenOcrService.Instance.ProcessBitmapAsync(bitmap, windowTitle);
                bitmap.Dispose();
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CccDesktopAdapter] ReadScreen error: {ex.Message}");
                return null;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static void KeyPress(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public void Dispose()
        {
            _typeItService.Dispose();
            _cccAutomation.Dispose();
        }
    }
}
