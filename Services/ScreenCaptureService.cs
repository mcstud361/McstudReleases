#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Captures screenshots of estimating application windows or the full screen.
    /// Auto-discovers CCC ONE / Mitchell / Audatex windows by title.
    /// </summary>
    public class ScreenCaptureService
    {
        private static ScreenCaptureService? _instance;
        public static ScreenCaptureService Instance => _instance ??= new ScreenCaptureService();

        // Window title patterns for estimating apps (matches CCCAutomationService)
        // Includes browser-based variants (Chrome, Edge) for CCC ONE web
        private static readonly string[] ESTIMATING_TITLES = new[]
        {
            "CCC ONE", "CCC Estimating", "CCCONE", "CCC Desktop",
            "caborneone", "caborneone.com",  // CCC ONE web URL
            "Mitchell", "Mitchell Cloud", "Mitchell International",
            "Audatex", "AudaExplore",
            "Estimate"
        };

        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Captures the foreground window. If McStud is in front, walks the Z-order
        /// to find the next visible window behind it (the one you were just working in).
        /// </summary>
        public (Bitmap? bitmap, string windowTitle) CaptureEstimatingWindow()
        {
            try
            {
                var hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero)
                {
                    StatusChanged?.Invoke(this, "No foreground window");
                    return (null, string.Empty);
                }

                // If McStud is in front, walk Z-order to find the next real window behind it
                if (IsOwnProcess(hWnd))
                {
                    hWnd = FindNextVisibleWindow(hWnd);
                    if (hWnd == IntPtr.Zero)
                    {
                        StatusChanged?.Invoke(this, "No window found behind McStud");
                        return (null, string.Empty);
                    }
                }

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                var title = sb.ToString();

                var bitmap = CaptureWindow(hWnd);
                if (bitmap != null)
                {
                    StatusChanged?.Invoke(this, $"Captured: {title}");
                    return (bitmap, title);
                }

                StatusChanged?.Invoke(this, "Capture failed");
                return (null, string.Empty);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Capture failed: {ex.Message}");
                return (null, string.Empty);
            }
        }

        /// <summary>
        /// Walks Z-order from the given window to find the next visible, titled,
        /// non-McStud window (i.e. the window the user was working in before switching to McStud).
        /// </summary>
        private IntPtr FindNextVisibleWindow(IntPtr startAfter)
        {
            var hWnd = GetWindow(startAfter, GW_HWNDNEXT);
            int checked_ = 0;

            while (hWnd != IntPtr.Zero && checked_ < 50)
            {
                checked_++;

                if (!IsWindowVisible(hWnd)) { hWnd = GetWindow(hWnd, GW_HWNDNEXT); continue; }

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) { hWnd = GetWindow(hWnd, GW_HWNDNEXT); continue; }

                if (IsOwnProcess(hWnd)) { hWnd = GetWindow(hWnd, GW_HWNDNEXT); continue; }

                // Skip tiny windows (tooltips, overlays, etc.)
                if (GetWindowRect(hWnd, out var rect) && rect.Width > 200 && rect.Height > 200)
                {
                    Debug.WriteLine($"[ScreenCapture] Found window behind McStud: \"{title}\"");
                    return hWnd;
                }

                hWnd = GetWindow(hWnd, GW_HWNDNEXT);
            }

            return IntPtr.Zero;
        }

        private static bool IsOwnProcess(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName.Equals("McstudDesktop", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Captures only the full primary screen.
        /// </summary>
        public Bitmap CaptureFullScreen()
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bitmap;
        }

        /// <summary>
        /// Captures a specific window by handle using PrintWindow.
        /// </summary>
        public Bitmap? CaptureWindow(IntPtr hWnd)
        {
            try
            {
                if (!GetWindowRect(hWnd, out var rect))
                    return null;

                if (rect.Width <= 0 || rect.Height <= 0)
                    return null;

                var bitmap = new Bitmap(rect.Width, rect.Height);
                using var graphics = Graphics.FromImage(bitmap);
                var hdc = graphics.GetHdc();

                // PW_RENDERFULLCONTENT = 2 for better capture of layered windows
                bool success = PrintWindow(hWnd, hdc, 2);
                graphics.ReleaseHdc(hdc);

                if (!success)
                {
                    // Fallback: use CopyFromScreen for the window area
                    bitmap.Dispose();
                    var fallback = new Bitmap(rect.Width, rect.Height);
                    using var g = Graphics.FromImage(fallback);
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height));
                    return fallback;
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenCapture] CaptureWindow failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds all visible estimating application windows.
        /// Returns list of (handle, title) tuples, prioritized by match quality.
        /// </summary>
        public List<(IntPtr hWnd, string title)> FindEstimatingWindows()
        {
            var results = new List<(IntPtr hWnd, string title, int priority)>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                var title = sb.ToString();

                if (string.IsNullOrWhiteSpace(title)) return true;

                // Skip our own app
                GetWindowThreadProcessId(hWnd, out uint processId);
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (process.ProcessName.Equals("McstudDesktop", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* process may have exited */ }

                // Check for estimating app titles
                for (int i = 0; i < ESTIMATING_TITLES.Length; i++)
                {
                    if (title.Contains(ESTIMATING_TITLES[i], StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add((hWnd, title, i)); // Lower index = higher priority
                        break;
                    }
                }

                return true;
            }, IntPtr.Zero);

            return results
                .OrderBy(r => r.priority)
                .Select(r => (r.hWnd, r.title))
                .ToList();
        }

        /// <summary>
        /// Detects which estimating platform a window title belongs to.
        /// </summary>
        public static Models.OcrEstimateSource DetectSource(string windowTitle)
        {
            if (string.IsNullOrEmpty(windowTitle))
                return Models.OcrEstimateSource.Unknown;

            if (windowTitle.Contains("CCC", StringComparison.OrdinalIgnoreCase))
                return Models.OcrEstimateSource.CCCOne;
            if (windowTitle.Contains("Mitchell", StringComparison.OrdinalIgnoreCase))
                return Models.OcrEstimateSource.Mitchell;
            if (windowTitle.Contains("Audatex", StringComparison.OrdinalIgnoreCase))
                return Models.OcrEstimateSource.Audatex;

            return Models.OcrEstimateSource.Unknown;
        }
    }
}
