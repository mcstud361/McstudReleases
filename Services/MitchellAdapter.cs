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
    /// Mitchell estimating system adapter — keyboard-only navigation.
    /// SupportsElementDiscovery = false.
    /// </summary>
    public class MitchellAdapter : IEstimatingSystemAdapter, IDisposable
    {
        private readonly TypeItService _typeItService;
        private IntPtr _mitchellWindow;
        private string _windowTitle = "";
        private bool _connected;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        public string SystemName => "Mitchell";
        public bool SupportsElementDiscovery => false;
        public bool IsConnected => _connected && _mitchellWindow != IntPtr.Zero;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<AutomationProgress>? ProgressChanged;

        public MitchellAdapter()
        {
            _typeItService = new TypeItService();
            _typeItService.SetSpeedLevel(3);
        }

        public bool CanHandle(OcrEstimateSource source) => source == OcrEstimateSource.Mitchell;

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                var windows = ScreenCaptureService.Instance.FindEstimatingWindows();
                foreach (var (hWnd, title) in windows)
                {
                    if (title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("UltraMate", StringComparison.OrdinalIgnoreCase))
                    {
                        _mitchellWindow = hWnd;
                        _windowTitle = title;
                        _connected = true;
                        StatusChanged?.Invoke(this, $"Connected to Mitchell: {title}");
                        return Task.FromResult(true);
                    }
                }

                StatusChanged?.Invoke(this, "Mitchell not found");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MitchellAdapter] Connect error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> FocusEstimatingWindowAsync(CancellationToken ct = default)
        {
            if (_mitchellWindow == IntPtr.Zero) return Task.FromResult(false);
            try
            {
                ShowWindow(_mitchellWindow, SW_RESTORE);
                SetForegroundWindow(_mitchellWindow);
                return Task.FromResult(true);
            }
            catch { return Task.FromResult(false); }
        }

        public async Task<bool> InsertNewLineAsync(CancellationToken ct = default)
        {
            // Mitchell: use right-click context menu same as CCC
            try
            {
                await _typeItService.InsertLineInCCCAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MitchellAdapter] InsertLine error: {ex.Message}");
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
            catch { return false; }
        }

        public Task<bool> TabToNextFieldAsync(CancellationToken ct = default)
        {
            try { _typeItService.PressTab(); return Task.FromResult(true); }
            catch { return Task.FromResult(false); }
        }

        public Task<bool> PressEnterAsync(CancellationToken ct = default)
        {
            try { _typeItService.PressEnter(); return Task.FromResult(true); }
            catch { return Task.FromResult(false); }
        }

        public Task<bool> PressEscapeAsync(CancellationToken ct = default)
        {
            return PressKeyAsync("Escape", ct);
        }

        public Task<bool> PressKeyAsync(string key, CancellationToken ct = default)
        {
            try
            {
                var vk = key.ToUpperInvariant() switch
                {
                    "ESCAPE" or "ESC" => (byte)0x1B,
                    "TAB" => (byte)0x09,
                    "ENTER" or "RETURN" => (byte)0x0D,
                    _ => (byte)0
                };
                if (vk == 0) return Task.FromResult(false);
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                return Task.FromResult(true);
            }
            catch { return Task.FromResult(false); }
        }

        public Task<bool> ClickElementAsync(string elementText, CancellationToken ct = default)
            => Task.FromResult(false);

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
                Debug.WriteLine($"[MitchellAdapter] ReadScreen error: {ex.Message}");
                return null;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public void Dispose()
        {
            _typeItService.Dispose();
        }
    }
}
