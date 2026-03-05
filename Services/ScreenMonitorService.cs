#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using McstudDesktop.Models;

namespace McstudDesktop.Services
{
    /// <summary>
    /// Orchestrates periodic screen capture and OCR processing.
    /// Manages capture interval, result history, and change detection.
    /// </summary>
    public class ScreenMonitorService
    {
        private static ScreenMonitorService? _instance;
        public static ScreenMonitorService Instance => _instance ??= new ScreenMonitorService();

        private readonly ScreenCaptureService _captureService;
        private readonly ScreenOcrService _ocrService;

        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private readonly List<ScreenOcrResult> _resultHistory = new();
        private readonly object _historyLock = new();

        private const int MaxHistorySize = 50;

        public event EventHandler<ScreenOcrResult>? OcrResultReady;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<bool>? MonitoringStateChanged;

        public bool IsMonitoring => _cts != null && !_cts.IsCancellationRequested;
        public TimeSpan CaptureInterval { get; set; } = TimeSpan.FromSeconds(2);
        public int CaptureCount { get; private set; }
        public int ChangeCount { get; private set; }
        public bool IsOcrAvailable => _ocrService.IsAvailable;

        public ScreenMonitorService()
        {
            _captureService = ScreenCaptureService.Instance;
            _ocrService = ScreenOcrService.Instance;
        }

        /// <summary>
        /// Starts periodic screen monitoring.
        /// </summary>
        public void Start()
        {
            if (IsMonitoring) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _monitorTask = Task.Run(async () =>
            {
                StatusChanged?.Invoke(this, "Monitoring started");
                MonitoringStateChanged?.Invoke(this, true);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await CaptureAndProcessAsync();
                        await Task.Delay(CaptureInterval, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ScreenMonitor] Error in monitor loop: {ex.Message}");
                        StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                        // Brief delay before retry to avoid tight error loops
                        try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
                    }
                }

                StatusChanged?.Invoke(this, "Monitoring stopped");
                MonitoringStateChanged?.Invoke(this, false);
            }, token);
        }

        /// <summary>
        /// Stops periodic screen monitoring.
        /// </summary>
        public void Stop()
        {
            if (!IsMonitoring) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            StatusChanged?.Invoke(this, "Monitoring stopped");
            MonitoringStateChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Performs a single capture and OCR cycle (for manual "Capture Once" button).
        /// </summary>
        public async Task<ScreenOcrResult> CaptureOnceAsync()
        {
            return await CaptureAndProcessAsync();
        }

        /// <summary>
        /// Gets the most recent OCR result (for paste services to query screen state).
        /// </summary>
        public ScreenOcrResult? GetLatestResult()
        {
            lock (_historyLock)
            {
                return _resultHistory.Count > 0 ? _resultHistory[^1] : null;
            }
        }

        /// <summary>
        /// Gets all results in history.
        /// </summary>
        public List<ScreenOcrResult> GetHistory()
        {
            lock (_historyLock)
            {
                return new List<ScreenOcrResult>(_resultHistory);
            }
        }

        /// <summary>
        /// Clears the result history and resets counters.
        /// </summary>
        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _resultHistory.Clear();
            }
            CaptureCount = 0;
            ChangeCount = 0;
            _ocrService.ResetChangeTracking();
            StatusChanged?.Invoke(this, "History cleared");
        }

        private async Task<ScreenOcrResult> CaptureAndProcessAsync()
        {
            // Capture screenshot
            var (bitmap, windowTitle) = _captureService.CaptureEstimatingWindow();

            if (bitmap == null)
            {
                var errorResult = new ScreenOcrResult
                {
                    ErrorMessage = "Screen capture failed",
                    Timestamp = DateTime.Now
                };
                AddToHistory(errorResult);
                OcrResultReady?.Invoke(this, errorResult);
                return errorResult;
            }

            try
            {
                // Run OCR
                var result = await _ocrService.ProcessBitmapAsync(bitmap, windowTitle);

                CaptureCount++;
                if (result.HasChanges) ChangeCount++;

                AddToHistory(result);
                OcrResultReady?.Invoke(this, result);

                return result;
            }
            finally
            {
                // Dispose bitmap immediately to prevent memory leaks
                bitmap.Dispose();
            }
        }

        private void AddToHistory(ScreenOcrResult result)
        {
            lock (_historyLock)
            {
                _resultHistory.Add(result);

                // Cap history size
                while (_resultHistory.Count > MaxHistorySize)
                {
                    _resultHistory.RemoveAt(0);
                }
            }
        }
    }
}
