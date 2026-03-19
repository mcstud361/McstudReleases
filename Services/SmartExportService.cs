#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Smart Export Service - Detects and exports to CCC Desktop, CCC Web, or Mitchell
    ///
    /// Features:
    /// - Auto-detect which estimating system is running
    /// - Format operations appropriately for each system
    /// - Keyboard automation to paste into the target application
    /// </summary>
    public class SmartExportService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        #endregion

        #region Events

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<ExportProgressEventArgs>? ProgressChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Delay between keystrokes (ms)
        /// </summary>
        public int KeyDelay { get; set; } = 30;

        /// <summary>
        /// Delay after Tab key (ms)
        /// </summary>
        public int TabDelay { get; set; } = 50;

        /// <summary>
        /// Delay after Enter key (ms)
        /// </summary>
        public int EnterDelay { get; set; } = 100;

        /// <summary>
        /// Initial delay after activating window (ms)
        /// </summary>
        public int ActivationDelay { get; set; } = 500;

        #endregion

        #region Target Detection

        public enum ExportTarget
        {
            None,
            CCCDesktop,
            CCCWeb,
            Mitchell,
            Unknown
        }

        /// <summary>
        /// Detect which estimating system windows are available
        /// </summary>
        public List<DetectedWindow> DetectEstimatingSystems()
        {
            var windows = new List<DetectedWindow>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, 512);
                string title = sb.ToString();

                if (string.IsNullOrEmpty(title)) return true;

                // Check for CCC ONE Desktop
                if (title.Contains("CCC ONE", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("CCC Estimating", StringComparison.OrdinalIgnoreCase) ||
                    (title.Contains("CCC", StringComparison.OrdinalIgnoreCase) &&
                     !title.Contains("McStud", StringComparison.OrdinalIgnoreCase) &&
                     !title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                     !title.Contains("Edge", StringComparison.OrdinalIgnoreCase) &&
                     !title.Contains("Firefox", StringComparison.OrdinalIgnoreCase)))
                {
                    windows.Add(new DetectedWindow
                    {
                        Handle = hWnd,
                        Title = title,
                        Target = ExportTarget.CCCDesktop
                    });
                }
                // Check for Mitchell Desktop
                else if (title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase) &&
                         !title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                         !title.Contains("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(new DetectedWindow
                    {
                        Handle = hWnd,
                        Title = title,
                        Target = ExportTarget.Mitchell
                    });
                }
                // Check for CCC Web (browser with CCC in title)
                else if ((title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                          title.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                          title.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) &&
                         title.Contains("CCC", StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(new DetectedWindow
                    {
                        Handle = hWnd,
                        Title = title,
                        Target = ExportTarget.CCCWeb
                    });
                }
                // Check for Mitchell Web (browser with Mitchell in title)
                else if ((title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                          title.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                          title.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) &&
                         title.Contains("Mitchell", StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(new DetectedWindow
                    {
                        Handle = hWnd,
                        Title = title,
                        Target = ExportTarget.Mitchell
                    });
                }

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        /// Get the best target window (prefer desktop apps over web)
        /// </summary>
        public DetectedWindow? GetBestTarget()
        {
            var windows = DetectEstimatingSystems();

            // Prefer desktop apps over web
            return windows.FirstOrDefault(w => w.Target == ExportTarget.CCCDesktop) ??
                   windows.FirstOrDefault(w => w.Target == ExportTarget.Mitchell) ??
                   windows.FirstOrDefault(w => w.Target == ExportTarget.CCCWeb) ??
                   windows.FirstOrDefault();
        }

        #endregion

        #region Export Operations

        /// <summary>
        /// Check if we have a valid click position for CCC export
        /// </summary>
        public bool HasClickPosition => McstudDesktop.Services.CCCInsertService.Instance.HasClickPosition;

        /// <summary>
        /// Get the target window title (where user last clicked)
        /// </summary>
        public string TargetWindowTitle => McstudDesktop.Services.CCCInsertService.Instance.TargetWindowTitle;

        /// <summary>
        /// Export operations to the specified target
        /// </summary>
        public async Task<ExportResult> ExportAsync(
            List<SmartExportOp> operations,
            ExportTarget target,
            IntPtr? targetWindow = null,
            CancellationToken cancellationToken = default)
        {
            if (operations.Count == 0)
            {
                return new ExportResult
                {
                    Success = false,
                    Message = "No operations to export"
                };
            }

            // For CCC Desktop, use CCCInsertService with click position
            if (target == ExportTarget.CCCDesktop)
            {
                var cccService = McstudDesktop.Services.CCCInsertService.Instance;
                if (!cccService.HasClickPosition)
                {
                    return new ExportResult
                    {
                        Success = false,
                        Message = "Click in CCC where you want to insert first, then come back here."
                    };
                }

                // Convert operations to the format CCCInsertService expects (tab-separated rows)
                var rows = FormatOperationsForCCC(operations);

                // Track result through events
                var tcs = new TaskCompletionSource<ExportResult>();
                int insertedCount = 0;

                void OnProgress(object? sender, McstudDesktop.Services.InsertProgressArgs e)
                {
                    insertedCount = e.Current;
                    ProgressChanged?.Invoke(this, new ExportProgressEventArgs
                    {
                        Current = e.Current,
                        Total = e.Total,
                        Message = $"Inserting {e.Current}/{e.Total}..."
                    });
                }

                void OnCompleted(object? sender, bool success)
                {
                    cccService.ProgressChanged -= OnProgress;
                    cccService.InsertCompleted -= OnCompleted;

                    tcs.TrySetResult(new ExportResult
                    {
                        Success = success,
                        Message = success ? $"Inserted {insertedCount} operations" : "Insert failed or cancelled",
                        ExportedCount = insertedCount
                    });
                }

                cccService.ProgressChanged += OnProgress;
                cccService.InsertCompleted += OnCompleted;

                StatusChanged?.Invoke(this, $"Inserting {operations.Count} operations into CCC...");

                // Start the insert (fire and forget - result comes through events)
                _ = cccService.InsertRawRowsAsync(rows, cancellationToken);

                // Wait for completion
                return await tcs.Task;
            }

            // Find target window if not specified (for non-CCC targets)
            IntPtr hwnd = targetWindow ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                var detected = DetectEstimatingSystems()
                    .FirstOrDefault(w => w.Target == target);

                if (detected == null)
                {
                    return new ExportResult
                    {
                        Success = false,
                        Message = $"Could not find {target} window. Make sure it's open."
                    };
                }
                hwnd = detected.Handle;
            }

            // Format operations for target system
            var formattedRows = FormatOperations(operations, target);

            // Run export on STA thread
            var staTaskCompletionSource = new TaskCompletionSource<ExportResult>();

            var thread = new Thread(() =>
            {
                try
                {
                    var result = PerformExport(hwnd, formattedRows, target, cancellationToken);
                    staTaskCompletionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    staTaskCompletionSource.SetResult(new ExportResult
                    {
                        Success = false,
                        Message = $"Export error: {ex.Message}"
                    });
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return await staTaskCompletionSource.Task;
        }

        /// <summary>
        /// Format operations for CCCInsertService (raw tab-separated Excel-like format)
        /// </summary>
        private List<string> FormatOperationsForCCC(List<SmartExportOp> operations)
        {
            var rows = new List<string>();
            foreach (var op in operations)
            {
                var row = string.Join("\t",
                    op.OperationType,
                    op.Description,
                    op.Quantity.ToString(),
                    op.Price > 0 ? op.Price.ToString("F2") : "0",
                    op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "0",
                    op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "0"
                );
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Quick export - copies formatted text to clipboard for manual paste
        /// </summary>
        public void QuickCopy(List<SmartExportOp> operations, ExportTarget target)
        {
            var formatted = FormatOperations(operations, target);
            var text = string.Join("\r\n", formatted);

            var thread = new Thread(() =>
            {
                Clipboard.SetText(text);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(1000);

            StatusChanged?.Invoke(this, $"Copied {operations.Count} operations for {target}");
        }

        #endregion

        #region Formatting

        /// <summary>
        /// Format operations for the target system
        /// </summary>
        private List<string> FormatOperations(List<SmartExportOp> operations, ExportTarget target)
        {
            var rows = new List<string>();

            foreach (var op in operations)
            {
                string row;
                switch (target)
                {
                    case ExportTarget.CCCDesktop:
                    case ExportTarget.CCCWeb:
                        // CCC Format: Operation, Description, Qty, Price, Labor, Paint
                        row = string.Join("\t",
                            op.OperationType,
                            op.Description,
                            op.Quantity.ToString(),
                            op.Price > 0 ? op.Price.ToString("F2") : "",
                            op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "",
                            op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : ""
                        );
                        break;

                    case ExportTarget.Mitchell:
                        // Mitchell Format: Operation, Description, Qty, Labor, Paint, Price
                        row = string.Join("\t",
                            op.OperationType,
                            op.Description,
                            op.Quantity.ToString(),
                            op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "",
                            op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "",
                            op.Price > 0 ? op.Price.ToString("F2") : ""
                        );
                        break;

                    default:
                        row = $"{op.Description}\t{op.LaborHours:F1}\t{op.RefinishHours:F1}\t{op.Price:F2}";
                        break;
                }
                rows.Add(row);
            }

            return rows;
        }

        #endregion

        #region Keyboard Automation

        /// <summary>
        /// Perform the actual export using keyboard automation
        /// </summary>
        private ExportResult PerformExport(IntPtr hwnd, List<string> rows, ExportTarget target, CancellationToken ct)
        {
            int exported = 0;

            try
            {
                // Activate target window
                ShowWindow(hwnd, SW_RESTORE);
                Thread.Sleep(100);
                SetForegroundWindow(hwnd);
                Thread.Sleep(ActivationDelay);

                StatusChanged?.Invoke(this, "Starting export...");

                foreach (var row in rows)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return new ExportResult
                        {
                            Success = false,
                            Message = $"Cancelled after {exported} operations",
                            ExportedCount = exported
                        };
                    }

                    // Parse the row back into fields
                    var fields = row.Split('\t');

                    if (target == ExportTarget.CCCDesktop)
                    {
                        // CCC Desktop: Right-click -> I for Insert Line, then type fields
                        SendKeys.SendWait("+{F10}"); // Right-click context menu
                        Thread.Sleep(100);
                        SendKeys.SendWait("i"); // Insert Line
                        Thread.Sleep(100);
                        SendKeys.SendWait("{ENTER}");
                        Thread.Sleep(200);

                        // Type each field with Tab between
                        foreach (var field in fields)
                        {
                            if (!string.IsNullOrEmpty(field))
                            {
                                // Use clipboard paste for reliability
                                Clipboard.SetText(field);
                                Thread.Sleep(10);
                                SendKeys.SendWait("^v");
                            }
                            SendKeys.SendWait("{TAB}");
                            Thread.Sleep(TabDelay);
                        }

                        SendKeys.SendWait("{ENTER}");
                        Thread.Sleep(EnterDelay);
                    }
                    else if (target == ExportTarget.CCCWeb)
                    {
                        // CCC Web: Paste entire row at once (assuming cursor is in right place)
                        Clipboard.SetText(row);
                        Thread.Sleep(10);
                        SendKeys.SendWait("^v");
                        Thread.Sleep(TabDelay);
                        SendKeys.SendWait("{ENTER}");
                        Thread.Sleep(EnterDelay);
                    }
                    else if (target == ExportTarget.Mitchell)
                    {
                        // Mitchell: Similar approach - paste row
                        Clipboard.SetText(row);
                        Thread.Sleep(10);
                        SendKeys.SendWait("^v");
                        Thread.Sleep(TabDelay);
                        SendKeys.SendWait("{ENTER}");
                        Thread.Sleep(EnterDelay);
                    }

                    exported++;
                    ProgressChanged?.Invoke(this, new ExportProgressEventArgs
                    {
                        Current = exported,
                        Total = rows.Count,
                        Message = $"Exporting {exported}/{rows.Count}..."
                    });
                }

                return new ExportResult
                {
                    Success = true,
                    Message = $"Exported {exported} operations",
                    ExportedCount = exported
                };
            }
            catch (Exception ex)
            {
                return new ExportResult
                {
                    Success = false,
                    Message = $"Error at row {exported + 1}: {ex.Message}",
                    ExportedCount = exported
                };
            }
        }

        #endregion
    }

    #region Data Classes

    public class DetectedWindow
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public SmartExportService.ExportTarget Target { get; set; }
    }

    public class SmartExportOp
    {
        public string OperationType { get; set; } = "Rpr";
        public string Description { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public decimal Price { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public string Category { get; set; } = "";
    }

    public class ExportProgressEventArgs : EventArgs
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string Message { get; set; } = "";
        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
    }

    #endregion
}
