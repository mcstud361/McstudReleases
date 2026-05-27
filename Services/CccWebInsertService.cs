#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using McstudDesktop.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace McStudDesktop.Services
{
    /// <summary>
    /// CCC Web Insert Service - Browser automation for CCC Web
    ///
    /// APPROACH (OCR + Keyboard Hybrid):
    /// 1. Alt+Tab to browser
    /// 2. Click at saved position to expand the line (action bar appears)
    /// 3. OCR screen region below click to find "Insert Line" button → click it
    /// 4. Tab through fields: Op Type, Description, Qty, Price, Labor, Paint
    /// 5. OCR to find "OK" button → click it
    /// 6. Repeat (reversed order since Insert Line inserts ABOVE)
    ///
    /// SAFETY: Low-level input hooks detect user mouse/keyboard activity.
    /// If user clicks, types, or presses Escape → sequence stops immediately.
    /// </summary>
    public class CccWebInsertService
    {
        #region Singleton

        private static CccWebInsertService? _instance;
        private static readonly object _lock = new object();

        public static CccWebInsertService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new CccWebInsertService();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint INPUT_KEYBOARD = 1;
        private const byte VK_TAB = 0x09;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_ESCAPE = 0x1B;
        private const byte VK_MENU = 0x12; // Alt key

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

        #endregion

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<InsertProgressArgs>? ProgressChanged;
        public event EventHandler<bool>? InsertCompleted;

        // State
        private bool _isInserting = false;
        private CancellationTokenSource? _cts;
        private IntPtr _targetBrowserWindow = IntPtr.Zero; // Set during InsertOperationsAsync for OCR filtering

        // Safety hook state
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelProc? _mouseProc;
        private LowLevelProc? _keyboardProc;
        private volatile bool _userInputDetected = false;
        private volatile bool _automationClicking = false; // True when our code is clicking

        // OCR engine (reused)
        private OcrEngine? _ocrEngine;

        public bool IsInserting => _isInserting;

        /// <summary>
        /// Number of Tabs to press to reach Op Type field after Insert Line.
        /// Default 0 (Op Type is auto-focused after insert).
        /// </summary>
        public int TabsToOpType { get; set; } = 0;

        // === Timing Delays (ms) ===
        public int ExpandDelay { get; set; } = 400;
        public int InsertLineDelay { get; set; } = 600;
        public int TabDelay { get; set; } = 50;
        public int CharDelay { get; set; } = 5;
        public int TypeaheadDelay { get; set; } = 150;
        public int OkDelay { get; set; } = 600;
        public int OcrDelay { get; set; } = 300; // Wait before OCR capture

        // === Position Offsets (px) ===
        // All offsets are pixels to the RIGHT of the Description field center.
        // Edit row layout: [Op Type] [Description] [Qty] [Price] [Labor] [Paint]
        public int QtyXFromDescription { get; set; } = 200;    // Click to open Extended Price popup
        public int LaborXFromDescription { get; set; } = 450;  // Labor hours field
        public int PaintXFromDescription { get; set; } = 550;  // Paint/Refinish hours field

        /// <summary>
        /// Set timing delays based on speed level (matches ExportPanel speed dropdown)
        /// 0=Slow, 1=Normal, 2=Fast, 3=Turbo, 4=Insane
        /// </summary>
        public void SetSpeedLevel(int level)
        {
            switch (level)
            {
                case 0: // Slow
                    ExpandDelay = 600; InsertLineDelay = 800; TabDelay = 80;
                    CharDelay = 15; TypeaheadDelay = 250; OkDelay = 800; OcrDelay = 500;
                    break;
                case 1: // Normal
                    ExpandDelay = 400; InsertLineDelay = 600; TabDelay = 50;
                    CharDelay = 5; TypeaheadDelay = 150; OkDelay = 600; OcrDelay = 300;
                    break;
                case 2: // Fast
                    ExpandDelay = 250; InsertLineDelay = 400; TabDelay = 30;
                    CharDelay = 3; TypeaheadDelay = 100; OkDelay = 400; OcrDelay = 200;
                    break;
                case 3: // Turbo
                    ExpandDelay = 150; InsertLineDelay = 250; TabDelay = 15;
                    CharDelay = 1; TypeaheadDelay = 60; OkDelay = 250; OcrDelay = 150;
                    break;
                case 4: // Insane
                    ExpandDelay = 80; InsertLineDelay = 150; TabDelay = 10;
                    CharDelay = 0; TypeaheadDelay = 40; OkDelay = 150; OcrDelay = 100;
                    break;
            }
        }

        // Operation type mapping: Our type -> CCC Web typeahead chars
        private static readonly Dictionary<string, string> OpTypeMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Rpr",      "Repa" },
            { "Repair",   "Repa" },
            { "Replace",  "Repl" },
            { "R&I",      "R&" },
            { "R+I",      "R&" },
            { "Blend",    "Bl" },
            { "Mat",      "Ref" },
            { "Refinish", "Ref" },
            { "Sublet",   "Su" },
            { "PDR",      "PD" },
            { "Align",    "Al" },
            { "Section",  "Se" },
        };

        public CccWebInsertService()
        {
            InitOcr();
        }

        private void InitOcr()
        {
            try
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                {
                    var english = new Windows.Globalization.Language("en-US");
                    if (OcrEngine.IsLanguageSupported(english))
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(english);
                }
                Debug.WriteLine($"[CCC-Web] OCR engine: {(_ocrEngine != null ? "ready" : "unavailable")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] OCR init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Main method: Insert operations into CCC Web via OCR + click automation.
        /// Uses Alt+Tab to switch back to browser (same approach as CCC Desktop).
        /// clickX/clickY = where user last clicked in CCC Web before switching to McStud.
        /// Operations are inserted in REVERSE order (Insert Line inserts ABOVE).
        /// </summary>
        public async Task InsertOperationsAsync(List<VirtualClipboardOp> ops, IntPtr targetWindow, int clickX, int clickY, CancellationToken cancellationToken = default)
        {
            if (ops.Count == 0)
            {
                StatusChanged?.Invoke(this, "No operations to insert");
                return;
            }

            if (_ocrEngine == null)
            {
                StatusChanged?.Invoke(this, "OCR engine unavailable - cannot find buttons");
                InsertCompleted?.Invoke(this, false);
                return;
            }

            _isInserting = true;
            _userInputDetected = false;
            _targetBrowserWindow = targetWindow;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Filter out empty/placeholder operations (blank description + all zeros)
            ops = ops.Where(o => !string.IsNullOrWhiteSpace(o.Description) ||
                                  o.Quantity > 0 || o.Price > 0 ||
                                  o.LaborHours > 0 || o.RefinishHours > 0).ToList();

            int totalOps = ops.Count;
            int successCount = 0;

            try
            {
                // Install safety hooks BEFORE automation starts
                InstallInputHooks();

                StatusChanged?.Invoke(this, $"Starting CCC Web insert ({totalOps} ops)...");
                Debug.WriteLine($"[CCC-Web] Click position: ({clickX}, {clickY})");

                // Bring browser to front using its actual window handle.
                // DO NOT use SendAltTab — it switches to whatever Windows thinks is
                // "previous," which could be ANY window, not necessarily the browser.
                Debug.WriteLine($"[CCC-Web] Bringing browser to front (target={targetWindow})...");
                bool switched = BringBrowserToFront(targetWindow);
                await Task.Delay(400, _cts.Token);
                if (!switched)
                {
                    Debug.WriteLine($"[CCC-Web] BringBrowserToFront failed, retrying...");
                    switched = BringBrowserToFront(targetWindow);
                    await Task.Delay(400, _cts.Token);
                }
                Debug.WriteLine($"[CCC-Web] After switch, foreground={GetForegroundWindow()}, success={switched}");

                // Get browser window rect — used to filter OCR results to ONLY the browser.
                // Without this, full-screen OCR picks up "Labor", "Paint", "0" from McStud's own UI,
                // causing clicks to land on the McStud window instead of the browser.
                GetWindowRect(targetWindow, out RECT browserRect);
                Debug.WriteLine($"[CCC-Web] Browser window rect: L={browserRect.Left} T={browserRect.Top} R={browserRect.Right} B={browserRect.Bottom}");

                // Browser CONTENT area bounds — everything above contentMinY is Chrome's
                // address bar, tabs, and bookmarks bar. Clicking there triggers Gemini search.
                int contentMinY = browserRect.Top + 130;
                int contentMaxY = browserRect.Bottom - 30;
                Debug.WriteLine($"[CCC-Web] Content area Y bounds: {contentMinY} to {contentMaxY}");

                // Check if user interrupted during Alt+Tab
                CheckUserInput();

                // Insert in ORDER — after each op, find last added text on screen and click below it.
                // Y-constrained OCR search avoids matching diagram panel text (e.g., "Cover Car").
                int lastEditRowY = 0;
                int lastEditRowX = 0; // Known-good X inside browser (from first op's Description)
                int lastInsertLineY = 0;
                string? lastAddedDescription = null;

                for (int i = 0; i < ops.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    CheckUserInput();

                    var op = ops[i];
                    int displayIndex = i + 1;

                    string opChars = GetTypeaheadChars(op.OperationType);
                    Debug.WriteLine($"[CCC-Web] === Op {displayIndex}/{totalOps}: {op.OperationType} -> '{opChars}' | {op.Description} ===");

                    ProgressChanged?.Invoke(this, new InsertProgressArgs(displayIndex, totalOps));
                    string shortDesc = op.Description.Length > 30 ? op.Description.Substring(0, 30) + "..." : op.Description;
                    StatusChanged?.Invoke(this, $"Op {displayIndex}/{totalOps}: {op.OperationType} - {shortDesc}");

                    // --- Retry wrapper: each operation gets up to 3 attempts ---
                    const int maxOpRetries = 3;
                    bool opSuccess = false;

                    for (int opAttempt = 0; opAttempt < maxOpRetries; opAttempt++)
                    {
                        if (_cts.Token.IsCancellationRequested) break;
                        CheckUserInput();

                        if (opAttempt > 0)
                        {
                            Debug.WriteLine($"[CCC-Web] Retry {opAttempt}/{maxOpRetries - 1} for op {displayIndex}");
                            StatusChanged?.Invoke(this, $"Op {displayIndex}/{totalOps}: Retry {opAttempt} — re-scanning estimate...");
                            // Press Escape to dismiss any stale dropdown/popup, then wait
                            SendKeyPress(VK_ESCAPE);
                            await Task.Delay(300, _cts.Token);

                            // RE-ORIENT: OCR the estimate to find the last successfully added op.
                            // Without this, retries blindly click the same stale coordinates.
                            if (lastAddedDescription != null)
                            {
                                var recoveryPos = await RecoverPositionFromEstimate(
                                    lastAddedDescription, successCount > 0 ? ops[successCount - 1].OperationType : null,
                                    contentMinY, contentMaxY);
                                if (recoveryPos != null)
                                {
                                    Debug.WriteLine($"[CCC-Web] Recovery: Found last op at ({recoveryPos.Value.nextX}, {recoveryPos.Value.nextY})");
                                    lastEditRowX = recoveryPos.Value.nextX;
                                    lastEditRowY = recoveryPos.Value.nextY;
                                }
                                else
                                {
                                    Debug.WriteLine($"[CCC-Web] Recovery: Could not find last op, scrolling to try...");
                                    int scrollX = (browserRect.Left + browserRect.Right) / 2;
                                    int scrollY = (browserRect.Top + browserRect.Bottom) / 2;
                                    ScrollDown(scrollX, scrollY, 2);
                                    await Task.Delay(400, _cts.Token);
                                    // Try recovery again after scroll
                                    recoveryPos = await RecoverPositionFromEstimate(
                                        lastAddedDescription, successCount > 0 ? ops[successCount - 1].OperationType : null,
                                        contentMinY, contentMaxY);
                                    if (recoveryPos != null)
                                    {
                                        Debug.WriteLine($"[CCC-Web] Recovery after scroll: Found at ({recoveryPos.Value.nextX}, {recoveryPos.Value.nextY})");
                                        lastEditRowX = recoveryPos.Value.nextX;
                                        lastEditRowY = recoveryPos.Value.nextY;
                                    }
                                }
                            }
                        }

                    // --- STEP 1 + 2: Expand a line and find Insert Line ---
                    System.Drawing.Point? insertLinePos = null;

                    if (i == 0 && opAttempt == 0)
                    {
                        // First op, first attempt — line is already expanded from user's click before switching to McStud
                        Debug.WriteLine($"[CCC-Web] Step 1: Skipped (first op — line already expanded)");
                        await Task.Delay(OcrDelay, _cts.Token);
                        insertLinePos = await FindButtonByOcr("Insert Line", clickX, clickY, searchBelow: true);
                        if (insertLinePos == null)
                            insertLinePos = await FindButtonByOcr("Insert", clickX, clickY, searchBelow: true);

                        // If Insert Line not visible (hidden behind diagrams panel), scroll down to reveal it.
                        // IMPORTANT: Scroll inside the BROWSER, not at clickX/clickY (which points to McStud).
                        if (insertLinePos == null)
                        {
                            int browserScrollX = (browserRect.Left + browserRect.Right) / 2;
                            int browserScrollY = (browserRect.Top + browserRect.Bottom) / 2;
                            Debug.WriteLine($"[CCC-Web] Insert Line not visible — scrolling browser at ({browserScrollX}, {browserScrollY})");
                            ScrollDown(browserScrollX, browserScrollY, 1);
                            await Task.Delay(400, _cts.Token);
                            insertLinePos = await FindButtonByOcr("Insert Line", clickX, clickY, searchBelow: true);
                            if (insertLinePos == null)
                                insertLinePos = await FindButtonByOcr("Insert", clickX, clickY, searchBelow: true);
                        }
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, $"Op {displayIndex}/{totalOps}: Finding next line...");

                        // Ensure the browser is the foreground window.
                        var currentFg = GetForegroundWindow();
                        if (currentFg != targetWindow && targetWindow != IntPtr.Zero)
                        {
                            Debug.WriteLine($"[CCC-Web] Browser lost focus, switching back...");
                            BringBrowserToFront(targetWindow);
                            await Task.Delay(400, _cts.Token);
                        }

                        // FAST PATH: Trust cached lastEditRowX/Y from the post-save scan.
                        // Only fall back to full recovery scan if clicking cached position fails.
                        int useX = lastEditRowX > 0 ? lastEditRowX : clickX;
                        int useY = lastEditRowY > 0 ? lastEditRowY : clickY;
                        Debug.WriteLine($"[CCC-Web] Target click: ({useX}, {useY}) [cached]");

                        // Try clicking the cached position first (no offset)
                        bool foundInsertLine = false;
                        int clampedY = Math.Max(contentMinY, Math.Min(contentMaxY, useY));
                        SetCursorPos(useX, clampedY);
                        await Task.Delay(30, _cts.Token);
                        LeftClick();
                        await Task.Delay(ExpandDelay + 200, _cts.Token);

                        // Check if Insert Line appeared
                        var checkWords = await OcrFullScreen();
                        if (_targetBrowserWindow != IntPtr.Zero)
                        {
                            GetWindowRect(_targetBrowserWindow, out RECT chkRect);
                            checkWords = checkWords.Where(w =>
                                w.x + w.w / 2 >= chkRect.Left && w.x + w.w / 2 <= chkRect.Right &&
                                w.y + w.h / 2 >= chkRect.Top && w.y + w.h / 2 <= chkRect.Bottom).ToList();
                        }
                        for (int wi = 0; wi < checkWords.Count - 1; wi++)
                        {
                            if (checkWords[wi].text.Equals("Insert", StringComparison.OrdinalIgnoreCase) &&
                                checkWords[wi + 1].text.Equals("Line", StringComparison.OrdinalIgnoreCase))
                            {
                                double cx = (checkWords[wi].x + checkWords[wi + 1].x + checkWords[wi + 1].w) / 2;
                                double cy = checkWords[wi].y + checkWords[wi].h / 2;
                                insertLinePos = new System.Drawing.Point((int)cx, (int)cy);
                                break;
                            }
                        }
                        if (insertLinePos != null)
                        {
                            Debug.WriteLine($"[CCC-Web] Found Insert Line on first click at ({insertLinePos.Value.X}, {insertLinePos.Value.Y})");
                            foundInsertLine = true;
                        }
                        else
                        {
                            // Dismiss any accidental popup from the failed click
                            SendKeyPress(VK_ESCAPE);
                            await Task.Delay(200, _cts.Token);
                        }

                        // CACHED CLICK FAILED — page likely scrolled or shifted.
                        // Now do a recovery scan to find the last added op on screen.
                        if (!foundInsertLine && lastAddedDescription != null)
                        {
                            Debug.WriteLine($"[CCC-Web] Cached position failed — scanning estimate for last added op...");
                            StatusChanged?.Invoke(this, $"Op {displayIndex}/{totalOps}: Re-locating last op...");
                            string prevOpType = (i > 0 && successCount > 0) ? ops[successCount - 1].OperationType : null;

                            var freshPos = await RecoverPositionFromEstimate(
                                lastAddedDescription, prevOpType, contentMinY, contentMaxY);

                            // If not found on screen, scroll to find it
                            if (freshPos == null)
                            {
                                Debug.WriteLine($"[CCC-Web] Last op not on screen, scrolling to find it...");
                                int scrollX = (browserRect.Left + browserRect.Right) / 2;
                                int scrollY = (browserRect.Top + browserRect.Bottom) / 2;

                                for (int scrollAttempt = 0; scrollAttempt < 3; scrollAttempt++)
                                {
                                    CheckUserInput();
                                    ScrollDown(scrollX, scrollY, scrollAttempt == 2 ? -3 : 2);
                                    await Task.Delay(400, _cts.Token);

                                    freshPos = await RecoverPositionFromEstimate(
                                        lastAddedDescription, prevOpType, contentMinY, contentMaxY);
                                    if (freshPos != null)
                                    {
                                        Debug.WriteLine($"[CCC-Web] Found last op after scroll attempt {scrollAttempt + 1}");
                                        break;
                                    }
                                }
                            }

                            if (freshPos != null)
                            {
                                useX = freshPos.Value.nextX;
                                useY = freshPos.Value.nextY;
                                lastEditRowX = useX;
                                lastEditRowY = useY;
                                Debug.WriteLine($"[CCC-Web] Recovery target: ({useX}, {useY})");

                                // Try clicking at recovered position with small offsets
                                int[] fineOffsets = { 0, 12, -12, 25, -25 };
                                for (int tryIdx = 0; tryIdx < fineOffsets.Length; tryIdx++)
                                {
                                    CheckUserInput();
                                    int tryY = Math.Max(contentMinY, Math.Min(contentMaxY,
                                        useY + fineOffsets[tryIdx]));
                                    Debug.WriteLine($"[CCC-Web] Recovery try {tryIdx + 1}: Click at ({useX}, {tryY})");
                                    SetCursorPos(useX, tryY);
                                    await Task.Delay(30, _cts.Token);
                                    LeftClick();
                                    await Task.Delay(ExpandDelay + 200, _cts.Token);

                                    var retryWords = await OcrFullScreen();
                                    if (_targetBrowserWindow != IntPtr.Zero)
                                    {
                                        GetWindowRect(_targetBrowserWindow, out RECT rRect);
                                        retryWords = retryWords.Where(w =>
                                            w.x + w.w / 2 >= rRect.Left && w.x + w.w / 2 <= rRect.Right &&
                                            w.y + w.h / 2 >= rRect.Top && w.y + w.h / 2 <= rRect.Bottom).ToList();
                                    }
                                    for (int wi = 0; wi < retryWords.Count - 1; wi++)
                                    {
                                        if (retryWords[wi].text.Equals("Insert", StringComparison.OrdinalIgnoreCase) &&
                                            retryWords[wi + 1].text.Equals("Line", StringComparison.OrdinalIgnoreCase))
                                        {
                                            double cx = (retryWords[wi].x + retryWords[wi + 1].x + retryWords[wi + 1].w) / 2;
                                            double cy = retryWords[wi].y + retryWords[wi].h / 2;
                                            insertLinePos = new System.Drawing.Point((int)cx, (int)cy);
                                            break;
                                        }
                                    }
                                    if (insertLinePos != null)
                                    {
                                        Debug.WriteLine($"[CCC-Web] Found Insert Line on recovery try {tryIdx + 1} at ({insertLinePos.Value.X}, {insertLinePos.Value.Y})");
                                        foundInsertLine = true;
                                        break;
                                    }
                                    SendKeyPress(VK_ESCAPE);
                                    await Task.Delay(200, _cts.Token);
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"[CCC-Web] Could not find last op after scrolling, using cached ({useX}, {useY})");
                            }
                        }

                        // LAST RESORT: If still not found, try small offsets from cached position
                        if (!foundInsertLine)
                        {
                            int[] lastOffsets = { 12, -12, 25, -25 };
                            for (int tryIdx = 0; tryIdx < lastOffsets.Length; tryIdx++)
                            {
                                CheckUserInput();
                                int tryY = Math.Max(contentMinY, Math.Min(contentMaxY,
                                    useY + lastOffsets[tryIdx]));
                                SetCursorPos(useX, tryY);
                                await Task.Delay(30, _cts.Token);
                                LeftClick();
                                await Task.Delay(ExpandDelay + 200, _cts.Token);

                                var fallbackWords = await OcrFullScreen();
                                if (_targetBrowserWindow != IntPtr.Zero)
                                {
                                    GetWindowRect(_targetBrowserWindow, out RECT fbRect);
                                    fallbackWords = fallbackWords.Where(w =>
                                        w.x + w.w / 2 >= fbRect.Left && w.x + w.w / 2 <= fbRect.Right &&
                                        w.y + w.h / 2 >= fbRect.Top && w.y + w.h / 2 <= fbRect.Bottom).ToList();
                                }
                                for (int wi = 0; wi < fallbackWords.Count - 1; wi++)
                                {
                                    if (fallbackWords[wi].text.Equals("Insert", StringComparison.OrdinalIgnoreCase) &&
                                        fallbackWords[wi + 1].text.Equals("Line", StringComparison.OrdinalIgnoreCase))
                                    {
                                        double cx = (fallbackWords[wi].x + fallbackWords[wi + 1].x + fallbackWords[wi + 1].w) / 2;
                                        double cy = fallbackWords[wi].y + fallbackWords[wi].h / 2;
                                        insertLinePos = new System.Drawing.Point((int)cx, (int)cy);
                                        break;
                                    }
                                }
                                if (insertLinePos != null)
                                {
                                    Debug.WriteLine($"[CCC-Web] Found Insert Line on last-resort try {tryIdx + 1}");
                                    foundInsertLine = true;
                                    break;
                                }
                                SendKeyPress(VK_ESCAPE);
                                await Task.Delay(200, _cts.Token);
                            }

                            if (!foundInsertLine)
                            {
                                Debug.WriteLine($"[CCC-Web] All click attempts failed for op {displayIndex}");
                            }
                        }
                    }
                    CheckUserInput();

                    if (insertLinePos == null)
                    {
                        Debug.WriteLine($"[CCC-Web] Could not find Insert Line on attempt {opAttempt + 1} for op {displayIndex}");
                        continue; // retry this operation
                    }

                    Debug.WriteLine($"[CCC-Web] Found Insert Line at ({insertLinePos.Value.X}, {insertLinePos.Value.Y})");
                    lastInsertLineY = insertLinePos.Value.Y;
                    SetCursorPos(insertLinePos.Value.X, insertLinePos.Value.Y);
                    await Task.Delay(30, _cts.Token);
                    LeftClick();
                    await Task.Delay(InsertLineDelay, _cts.Token);
                    CheckUserInput();

                    // --- STEP 3: OCR to find ALL edit row fields ---
                    // Pass insertLinePos.Y as hint — pick "Description" closest to where Insert Line was
                    Debug.WriteLine($"[CCC-Web] Step 3: OCR to find edit row fields (near Y={insertLinePos.Value.Y})...");
                    await Task.Delay(OcrDelay, _cts.Token);
                    var fields = await FindEditRowFields(insertLinePos.Value.Y);
                    if (fields == null || fields.Description == null)
                    {
                        Debug.WriteLine($"[CCC-Web] Could not find edit row fields on attempt {opAttempt + 1}");
                        continue; // retry this operation
                    }

                    // Verify: Description should be reasonably near Insert Line Y.
                    // Column headers are typically 300+ px away from the operations area.
                    int descToInsertDist = Math.Abs(fields.Description.Value.Y - insertLinePos.Value.Y);
                    Debug.WriteLine($"[CCC-Web] Verification: Description Y={fields.Description.Value.Y}, Insert Line Y={insertLinePos.Value.Y}, dist={descToInsertDist}");
                    if (descToInsertDist > 300)
                    {
                        Debug.WriteLine($"[CCC-Web] WARNING: Description too far from Insert Line — likely a column header false match");
                        continue; // retry this operation
                    }
                    CheckUserInput();

                    // --- STEP 4: Select Op Type via typeahead ---
                    // Typeahead is more reliable than OCR for dropdown selection:
                    // - OCR searches full screen and can click false matches (column headers, existing ops)
                    // - OCR misreads "&" and "I" in "R&I" frequently
                    // - Typeahead directly filters the dropdown to the correct option
                    int opTypeX = fields.OpType?.X ?? (fields.Description.Value.X - 150);
                    int opTypeY = fields.OpType?.Y ?? fields.Description.Value.Y;
                    Debug.WriteLine($"[CCC-Web] Step 4: Click Op Type at ({opTypeX}, {opTypeY})");
                    SetCursorPos(opTypeX, opTypeY);
                    await Task.Delay(30, _cts.Token);
                    LeftClick();
                    await Task.Delay(ExpandDelay, _cts.Token);
                    CheckUserInput();

                    // Type to filter the dropdown and select the option
                    string typeaheadChars = GetTypeaheadChars(op.OperationType);
                    Debug.WriteLine($"[CCC-Web] Step 4b: Typeahead '{typeaheadChars}' for '{op.OperationType}'");
                    TypeText(typeaheadChars);
                    await Task.Delay(TypeaheadDelay, _cts.Token);
                    SendKeyPress(VK_RETURN);
                    await Task.Delay(TabDelay * 2, _cts.Token);
                    CheckUserInput();

                    // --- STEP 5: Click Description field and type ---
                    Debug.WriteLine($"[CCC-Web] Step 5: Click Description at ({fields.Description.Value.X}, {fields.Description.Value.Y})");
                    SetCursorPos(fields.Description.Value.X, fields.Description.Value.Y);
                    await Task.Delay(30, _cts.Token);
                    LeftClick();
                    await Task.Delay(TabDelay, _cts.Token);
                    if (!string.IsNullOrEmpty(op.Description))
                    {
                        TypeText(op.Description);
                    }
                    CheckUserInput();

                    // --- STEP 5b: Fresh OCR to refresh field positions ---
                    // Use EnsureEditRowVisible to find (and scroll to) the edit row.
                    int editRowY = fields.Description.Value.Y;
                    Debug.WriteLine($"[CCC-Web] Step 5b: Refreshing field positions (editRowY={editRowY})...");
                    await Task.Delay(OcrDelay, _cts.Token);

                    var ensureResult = await EnsureEditRowVisible(op.Description, editRowY, targetWindow);
                    if (ensureResult != null)
                    {
                        editRowY = ensureResult.Value.liveY;
                        fields.Description = new System.Drawing.Point(ensureResult.Value.liveX, editRowY);
                    }

                    // Grab fresh words for field detection below
                    var freshWords = await OcrFullScreen();
                    GetWindowRect(targetWindow, out RECT freshBrowserRect);
                    freshWords = freshWords.Where(w =>
                        w.x + w.w / 2 >= freshBrowserRect.Left &&
                        w.x + w.w / 2 <= freshBrowserRect.Right &&
                        w.y + w.h / 2 >= freshBrowserRect.Top &&
                        w.y + w.h / 2 <= freshBrowserRect.Bottom).ToList();

                    double bestFreshQtyDist = double.MaxValue;
                    double bestFreshPriceDist = double.MaxValue;
                    System.Drawing.Point? freshQty = null;
                    System.Drawing.Point? freshPrice = null;

                    foreach (var fw in freshWords)
                    {
                        double fwx = fw.x + fw.w / 2;
                        double fwy = fw.y + fw.h / 2;
                        double distFromRow = Math.Abs(fwy - editRowY);

                        if ((fw.text.Equals("Qty", StringComparison.OrdinalIgnoreCase) ||
                             fw.text.Equals("Quantity", StringComparison.OrdinalIgnoreCase)) &&
                            fwx > fields.Description.Value.X)
                        {
                            if (distFromRow < bestFreshQtyDist)
                            {
                                bestFreshQtyDist = distFromRow;
                                freshQty = new System.Drawing.Point((int)fwx, (int)fwy);
                            }
                        }
                        else if (fw.text.Equals("Price", StringComparison.OrdinalIgnoreCase) &&
                                 fwx > fields.Description.Value.X)
                        {
                            if (distFromRow < bestFreshPriceDist)
                            {
                                bestFreshPriceDist = distFromRow;
                                freshPrice = new System.Drawing.Point((int)fwx, (int)fwy);
                            }
                        }
                    }

                    if (freshQty != null)
                    {
                        Debug.WriteLine($"[CCC-Web] Fresh Qty at ({freshQty.Value.X}, {freshQty.Value.Y}) — was ({fields.Qty?.X}, {fields.Qty?.Y})");
                        fields.Qty = freshQty;
                        editRowY = freshQty.Value.Y;
                    }
                    if (freshPrice != null)
                    {
                        Debug.WriteLine($"[CCC-Web] Fresh Price at ({freshPrice.Value.X}, {freshPrice.Value.Y}) — was ({fields.Price?.X}, {fields.Price?.Y})");
                        fields.Price = freshPrice;
                        editRowY = freshPrice.Value.Y;
                    }

                    // PRIMARY: Find the "0" default values on the edit row.
                    // Layout: [Op Type] [Description] [Qty] [Price] [0=Labor] [0=Refinish]
                    // The two rightmost "0" values ARE the Labor and Refinish fields.
                    // Browser filtering prevents matching "0"s from McStud's own UI.
                    // Y tolerance of ±20px ensures we only get zeros on THIS edit row.
                    var zeroFields = freshWords
                        .Where(w => (w.text == "0" || w.text == "O" || w.text == "o") &&
                            Math.Abs((w.y + w.h / 2) - editRowY) < 20 &&
                            (w.x + w.w / 2) > fields.Description.Value.X + 100) // Must be right of description
                        .OrderBy(w => w.x) // Left to right
                        .ToList();
                    Debug.WriteLine($"[CCC-Web] Found {zeroFields.Count} zero-fields on edit row (Y={editRowY}±20, X>{fields.Description.Value.X + 100})");
                    foreach (var zf in zeroFields)
                        Debug.WriteLine($"[CCC-Web]   '0' at X={zf.x + zf.w / 2:F0}, Y={zf.y + zf.h / 2:F0}");

                    if (zeroFields.Count >= 2)
                    {
                        // First "0" = Body Labor, Last "0" = Refinish Labor
                        var laborZero = zeroFields[zeroFields.Count - 2];
                        var refinishZero = zeroFields[zeroFields.Count - 1];
                        fields.Labor = new System.Drawing.Point((int)(laborZero.x + laborZero.w / 2), editRowY);
                        fields.Paint = new System.Drawing.Point((int)(refinishZero.x + refinishZero.w / 2), editRowY);
                        Debug.WriteLine($"[CCC-Web] Zero-based: Labor X={fields.Labor.Value.X}, Refinish X={fields.Paint.Value.X}");
                    }
                    else if (zeroFields.Count == 1)
                    {
                        // Only one "0" found — assume it's the rightmost (Refinish)
                        var singleZero = zeroFields[0];
                        double zx = singleZero.x + singleZero.w / 2;
                        fields.Paint = new System.Drawing.Point((int)zx, editRowY);
                        fields.Labor = new System.Drawing.Point((int)(zx - 85), editRowY);
                        Debug.WriteLine($"[CCC-Web] Single zero → Refinish X={fields.Paint.Value.X}, Labor X={fields.Labor.Value.X}");
                    }

                    // FALLBACK: If no "0" found, try Cancel/OK button positions on the action bar
                    if (fields.Labor == null || fields.Paint == null)
                    {
                        System.Drawing.Point? actionBarCancel = null;
                        System.Drawing.Point? actionBarOk = null;
                        double bestCancelDist = double.MaxValue;
                        double bestOkDist = double.MaxValue;

                        foreach (var fw in freshWords)
                        {
                            double fwx = fw.x + fw.w / 2;
                            double fwy = fw.y + fw.h / 2;
                            if (fwy < editRowY + 40 || fwy > editRowY + 150) continue;

                            if (fw.text.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                            {
                                double dist = Math.Abs(fwy - (editRowY + 95));
                                if (dist < bestCancelDist)
                                { bestCancelDist = dist; actionBarCancel = new System.Drawing.Point((int)fwx, (int)fwy); }
                            }
                            else if (fw.text.Equals("OK", StringComparison.OrdinalIgnoreCase) && fwx > fields.Description.Value.X)
                            {
                                double dist = Math.Abs(fwy - (editRowY + 95));
                                if (dist < bestOkDist)
                                { bestOkDist = dist; actionBarOk = new System.Drawing.Point((int)fwx, (int)fwy); }
                            }
                        }

                        if (actionBarCancel != null)
                        {
                            fields.Paint = fields.Paint ?? new System.Drawing.Point(actionBarCancel.Value.X, editRowY);
                            fields.Labor = fields.Labor ?? new System.Drawing.Point(actionBarCancel.Value.X - 85, editRowY);
                            Debug.WriteLine($"[CCC-Web] Cancel fallback → Labor X={fields.Labor.Value.X}, Refinish X={fields.Paint.Value.X}");
                        }
                        else if (actionBarOk != null)
                        {
                            fields.Labor = fields.Labor ?? new System.Drawing.Point(actionBarOk.Value.X, editRowY);
                            fields.Paint = fields.Paint ?? new System.Drawing.Point(actionBarOk.Value.X + 85, editRowY);
                            Debug.WriteLine($"[CCC-Web] OK fallback → Labor X={fields.Labor.Value.X}, Refinish X={fields.Paint.Value.X}");
                        }
                        else if (freshPrice != null)
                        {
                            fields.Labor = fields.Labor ?? new System.Drawing.Point(freshPrice.Value.X + 120, editRowY);
                            fields.Paint = fields.Paint ?? new System.Drawing.Point(freshPrice.Value.X + 200, editRowY);
                            Debug.WriteLine($"[CCC-Web] Price fallback → Labor X={fields.Labor.Value.X}, Refinish X={fields.Paint.Value.X}");
                        }
                    }
                    CheckUserInput();

                    // --- STEP 6: Qty + Price (click Qty → popup → Tab to Price → Enter) ---
                    if (op.Price > 0 || op.Quantity > 1)
                    {
                        // Ensure edit row is visible before clicking Qty
                        var step6Pos = await EnsureEditRowVisible(op.Description, editRowY, targetWindow);
                        if (step6Pos != null) { editRowY = step6Pos.Value.liveY; fields.Description = new System.Drawing.Point(step6Pos.Value.liveX, editRowY); }

                        int qtyX = fields.Qty?.X ?? (fields.Description.Value.X + QtyXFromDescription);
                        int qtyY = fields.Qty?.Y ?? editRowY;
                        Debug.WriteLine($"[CCC-Web] Step 6: Click Qty at ({qtyX}, {qtyY}) → open popup");
                        SetCursorPos(qtyX, qtyY);
                        await Task.Delay(30, _cts.Token);
                        LeftClick();
                        await Task.Delay(ExpandDelay, _cts.Token);

                        string qtyText = op.Quantity > 0 ? op.Quantity.ToString() : "1";
                        Debug.WriteLine($"[CCC-Web] Step 6b: Type Qty='{qtyText}' in popup");
                        SelectAllAndType(qtyText);
                        await Task.Delay(200, _cts.Token);

                        Debug.WriteLine($"[CCC-Web] Step 6c: Tab → Price");
                        SendKeyPress(VK_TAB);
                        await Task.Delay(300, _cts.Token);

                        string priceText = op.Price > 0 ? op.Price.ToString("0.##") : "0";
                        Debug.WriteLine($"[CCC-Web] Step 6d: Type Price='{priceText}' in popup");
                        SelectAllAndType(priceText);
                        await Task.Delay(200, _cts.Token);

                        Debug.WriteLine($"[CCC-Web] Step 6e: Enter to close popup");
                        SendKeyPress(VK_RETURN);
                        await Task.Delay(ExpandDelay + 200, _cts.Token);
                    }
                    CheckUserInput();

                    // --- STEP 7: Labor (click directly, no popup) ---
                    if (op.LaborHours > 0)
                    {
                        // Ensure edit row is visible before clicking Labor
                        var step7Pos = await EnsureEditRowVisible(op.Description, editRowY, targetWindow);
                        if (step7Pos != null) { editRowY = step7Pos.Value.liveY; fields.Description = new System.Drawing.Point(step7Pos.Value.liveX, editRowY); }

                        int laborX = fields.Labor?.X ?? (fields.Description.Value.X + LaborXFromDescription);
                        int laborY = editRowY; // Always use live Y, not stale fields.Labor.Y
                        Debug.WriteLine($"[CCC-Web] Step 7: Click Labor at ({laborX}, {laborY}), type '{op.LaborHours}'");
                        SetCursorPos(laborX, laborY);
                        await Task.Delay(50, _cts.Token);
                        LeftClick();
                        await Task.Delay(100, _cts.Token);
                        SelectAllAndType(op.LaborHours.ToString("0.##"));
                        await Task.Delay(100, _cts.Token);
                    }

                    // --- STEP 8: Paint/Refinish (click directly, no popup) ---
                    if (op.RefinishHours > 0)
                    {
                        // Ensure edit row is visible before clicking Refinish
                        var step8Pos = await EnsureEditRowVisible(op.Description, editRowY, targetWindow);
                        if (step8Pos != null) { editRowY = step8Pos.Value.liveY; fields.Description = new System.Drawing.Point(step8Pos.Value.liveX, editRowY); }

                        int paintX = fields.Paint?.X ?? (fields.Description.Value.X + PaintXFromDescription);
                        int paintY = editRowY; // Always use live Y
                        Debug.WriteLine($"[CCC-Web] Step 8: Click Refinish at ({paintX}, {paintY}), type '{op.RefinishHours}'");
                        SetCursorPos(paintX, paintY);
                        await Task.Delay(50, _cts.Token);
                        LeftClick();
                        await Task.Delay(100, _cts.Token);
                        SelectAllAndType(op.RefinishHours.ToString("0.##"));
                        await Task.Delay(100, _cts.Token);
                    }
                    CheckUserInput();

                    // --- STEP 9: Click OK button ---
                    // Ensure edit row visible so we can find OK/Cancel.
                    var step9Pos = await EnsureEditRowVisible(op.Description, editRowY, targetWindow);
                    if (step9Pos != null) { editRowY = step9Pos.Value.liveY; fields.Description = new System.Drawing.Point(step9Pos.Value.liveX, editRowY); }

                    // Fresh OCR scan to find OK or Cancel on the action bar.
                    // OK is blue (hard for OCR), Cancel is dark-on-white (easier).
                    // If we find Cancel, OK is 85px to its left.
                    //
                    // IMPORTANT: If the user scrolled during field entry, the action bar
                    // may have moved to a completely different Y position. We search in
                    // two passes: first near the expected editRowY, then anywhere in the
                    // browser if that fails.
                    Debug.WriteLine($"[CCC-Web] Step 9: Finding OK button...");
                    await Task.Delay(OcrDelay, _cts.Token);

                    System.Drawing.Point? okPos = null;
                    System.Drawing.Point? cancelPos = null;

                    // Helper: scan OCR words for OK/Cancel, optionally constrained to Y range
                    void SearchForOkCancel(List<(string text, double x, double y, double w, double h)> words,
                        int? minY, int? maxY)
                    {
                        double bestOkDist = double.MaxValue;
                        double bestCancelDist = double.MaxValue;
                        foreach (var w9 in words)
                        {
                            double wy = w9.y + w9.h / 2;
                            double wx = w9.x + w9.w / 2;
                            if (minY != null && wy < minY.Value) continue;
                            if (maxY != null && wy > maxY.Value) continue;

                            if (w9.text.Equals("OK", StringComparison.OrdinalIgnoreCase))
                            {
                                double dist = Math.Abs(wy - editRowY);
                                if (dist < bestOkDist)
                                {
                                    bestOkDist = dist;
                                    okPos = new System.Drawing.Point((int)wx, (int)wy);
                                }
                            }
                            else if (w9.text.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                            {
                                double dist = Math.Abs(wy - editRowY);
                                if (dist < bestCancelDist)
                                {
                                    bestCancelDist = dist;
                                    cancelPos = new System.Drawing.Point((int)wx, (int)wy);
                                }
                            }
                        }
                    }

                    var step9Words = await OcrFullScreen();
                    GetWindowRect(targetWindow, out RECT okBrowserRect);
                    step9Words = step9Words.Where(w =>
                        w.x + w.w / 2 >= okBrowserRect.Left && w.x + w.w / 2 <= okBrowserRect.Right &&
                        w.y + w.h / 2 >= okBrowserRect.Top && w.y + w.h / 2 <= okBrowserRect.Bottom).ToList();

                    // Pass 1: Search near expected Y range (action bar is 40-150px below edit row)
                    SearchForOkCancel(step9Words, editRowY + 40, editRowY + 150);

                    if (okPos != null)
                        Debug.WriteLine($"[CCC-Web] Step 9: Found OK at ({okPos.Value.X}, {okPos.Value.Y}) [near expected Y]");
                    if (cancelPos != null)
                        Debug.WriteLine($"[CCC-Web] Step 9: Found Cancel at ({cancelPos.Value.X}, {cancelPos.Value.Y}) [near expected Y]");

                    // Pass 2: If nothing found (user scrolled), search ANYWHERE in browser
                    if (okPos == null && cancelPos == null)
                    {
                        Debug.WriteLine($"[CCC-Web] Step 9: OK/Cancel not near editRowY={editRowY} — searching full browser (page may have scrolled)");
                        SearchForOkCancel(step9Words, null, null);

                        if (okPos != null)
                            Debug.WriteLine($"[CCC-Web] Step 9: Found OK at ({okPos.Value.X}, {okPos.Value.Y}) [full browser search]");
                        if (cancelPos != null)
                            Debug.WriteLine($"[CCC-Web] Step 9: Found Cancel at ({cancelPos.Value.X}, {cancelPos.Value.Y}) [full browser search]");
                    }

                    if (okPos != null)
                    {
                        SetCursorPos(okPos.Value.X, okPos.Value.Y);
                        await Task.Delay(30, _cts.Token);
                        LeftClick();
                    }
                    else if (cancelPos != null)
                    {
                        int derivedOkX = cancelPos.Value.X - 85;
                        Debug.WriteLine($"[CCC-Web] Deriving OK from Cancel → ({derivedOkX}, {cancelPos.Value.Y})");
                        SetCursorPos(derivedOkX, cancelPos.Value.Y);
                        await Task.Delay(30, _cts.Token);
                        LeftClick();
                    }
                    else
                    {
                        // OK/Cancel not visible at all — use Enter to submit the form.
                        // Enter works regardless of scroll position since the edit row
                        // still has keyboard focus even if it scrolled off-screen.
                        Debug.WriteLine($"[CCC-Web] OK/Cancel not found anywhere — pressing Enter to submit");
                        SendKeyPress(VK_RETURN);
                    }
                    await Task.Delay(OkDelay, _cts.Token);

                    // Wait for CCC Web to finish saving — poll OCR until "Cancel" disappears.
                    // Search the ENTIRE browser (no Y constraint) because the page may have
                    // scrolled and Cancel could be anywhere.
                    for (int readyCheck = 0; readyCheck < 8; readyCheck++)
                    {
                        CheckUserInput();
                        var readyWords = await OcrFullScreen();
                        GetWindowRect(targetWindow, out RECT readyRect);
                        bool cancelStillVisible = readyWords.Any(w =>
                            w.text.Equals("Cancel", StringComparison.OrdinalIgnoreCase) &&
                            (w.x + w.w / 2) >= readyRect.Left &&
                            (w.x + w.w / 2) <= readyRect.Right &&
                            (w.y + w.h / 2) >= readyRect.Top &&
                            (w.y + w.h / 2) <= readyRect.Bottom);
                        if (!cancelStillVisible)
                        {
                            Debug.WriteLine($"[CCC-Web] Edit mode closed (Cancel gone) after {readyCheck + 1} checks");
                            break;
                        }
                        Debug.WriteLine($"[CCC-Web] Still in edit mode (Cancel visible), waiting... ({readyCheck + 1}/8)");
                        await Task.Delay(300, _cts.Token);
                    }
                    CheckUserInput();

                    successCount++;
                    lastEditRowX = fields.Description.Value.X;
                    lastEditRowY = fields.Description.Value.Y;
                    lastAddedDescription = op.Description;

                    // OCR-scan to find the collapsed row of the op we just added,
                    // then find the NEXT line below it (that's where we'll click for the next op).
                    //
                    // STRATEGY: Multi-word description match + op type cross-reference.
                    // Single-word matching is fragile — "Bumper", "Cover", etc. appear in
                    // multiple rows and diagram panels. Using 2+ description words AND
                    // verifying the op type on the same row gives a confident match.
                    try
                    {
                        await Task.Delay(400, _cts.Token);
                        var postSaveWords = await OcrFullScreen();
                        if (_targetBrowserWindow != IntPtr.Zero)
                        {
                            GetWindowRect(_targetBrowserWindow, out RECT psRect);
                            postSaveWords = postSaveWords.Where(w =>
                                w.x + w.w / 2 >= psRect.Left && w.x + w.w / 2 <= psRect.Right &&
                                w.y + w.h / 2 >= psRect.Top && w.y + w.h / 2 <= psRect.Bottom).ToList();
                        }

                        // Build search words from description (use first 3 usable words)
                        string[] descWords = op.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var searchWords = descWords.Where(w => w.Length >= 2).Take(3).ToArray();
                        string primaryWord = searchWords.Length > 0 ? searchWords[0] : "";
                        // Use second word as primary if first is too short (e.g., "RT", "LT")
                        if (primaryWord.Length < 3 && searchWords.Length > 1)
                        {
                            primaryWord = searchWords[1];
                            searchWords = searchWords.Skip(1).ToArray();
                        }

                        // Also get the CCC Web op type name for cross-referencing
                        string expectedOpType = GetCccWebOpName(op.OperationType);

                        int foundRowY = -1;
                        int foundRowX = -1;

                        if (primaryWord.Length >= 2)
                        {
                            // Find all matches of the primary word in the content area
                            var candidates = postSaveWords
                                .Where(w => w.text.IndexOf(primaryWord, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    w.y + w.h / 2 >= contentMinY && w.y + w.h / 2 <= contentMaxY)
                                .ToList();

                            // Score each candidate by how many description words + op type match on the same row
                            var scoredCandidates = new List<(int score, double y, double x, string matchInfo)>();

                            foreach (var cand in candidates)
                            {
                                double candY = cand.y + cand.h / 2;
                                double candX = cand.x + cand.w / 2;
                                int score = 1; // 1 point for primary word match
                                string matchInfo = $"'{primaryWord}'";

                                // Check for additional description words on the same row (±15px Y)
                                for (int sw = 1; sw < searchWords.Length; sw++)
                                {
                                    bool hasWord = postSaveWords.Any(w =>
                                        w.text.IndexOf(searchWords[sw], StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        Math.Abs((w.y + w.h / 2) - candY) < 15 &&
                                        (w.x + w.w / 2) > candX - 50); // Allow slightly left
                                    if (hasWord)
                                    {
                                        score += 2; // 2 points for each additional word
                                        matchInfo += $"+'{searchWords[sw]}'";
                                    }
                                }

                                // Check for op type on the same row, to the LEFT of the description
                                bool hasOpType = postSaveWords.Any(w =>
                                    w.text.IndexOf(expectedOpType, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    Math.Abs((w.y + w.h / 2) - candY) < 15 &&
                                    (w.x + w.w / 2) < candX); // Must be LEFT of description
                                if (hasOpType)
                                {
                                    score += 5; // 5 points for op type confirmation
                                    matchInfo += $"+OpType({expectedOpType})";
                                }

                                // Proximity to last edit position (closer = slight bonus)
                                double distFromEdit = Math.Abs(candY - lastEditRowY);

                                scoredCandidates.Add((score, candY, candX, matchInfo));
                                Debug.WriteLine($"[CCC-Web] PostSave candidate: Y={candY:F0} score={score} dist={distFromEdit:F0} [{matchInfo}]");
                            }

                            if (scoredCandidates.Count > 0)
                            {
                                // Pick highest score, break ties by closest to lastEditRowY
                                var best = scoredCandidates
                                    .OrderByDescending(c => c.score)
                                    .ThenBy(c => Math.Abs(c.y - lastEditRowY))
                                    .First();

                                foundRowY = (int)best.y;
                                foundRowX = (int)best.x;
                                Debug.WriteLine($"[CCC-Web] Best match: Y={foundRowY} score={best.score} [{best.matchInfo}] [edit was at Y={lastEditRowY}]");
                            }
                        }

                        if (foundRowY > 0)
                        {
                            // Find the NEXT distinct operation row below our found row.
                            // Look for rows that contain op type keywords (Replace, Repair, R&I, etc.)
                            // which are always visible on collapsed CCC Web rows.
                            // This avoids matching diagram text, action bar remnants, or headers.
                            var opTypeKeywords = new[] { "Replace", "Repair", "R&I", "R&l", "Blend", "Refinish", "Sublet", "PDR", "Align", "Section", "R&i" };

                            // First try: find next row with an op type keyword
                            var nextOpRows = postSaveWords
                                .Where(w => (w.y + w.h / 2) > foundRowY + 12 &&
                                            (w.y + w.h / 2) <= contentMaxY &&
                                            opTypeKeywords.Any(k => w.text.Equals(k, StringComparison.OrdinalIgnoreCase)))
                                .OrderBy(w => w.y)
                                .ToList();

                            if (nextOpRows.Count > 0)
                            {
                                int nextLineY = (int)(nextOpRows[0].y + nextOpRows[0].h / 2);
                                // Use the X position from that op type word — it's reliably in the op row
                                // But for clicking, we want the description column X, so keep foundRowX
                                Debug.WriteLine($"[CCC-Web] Next op row (by op type keyword '{nextOpRows[0].text}') at Y={nextLineY} (gap={nextLineY - foundRowY}px)");
                                lastEditRowY = nextLineY;
                                lastEditRowX = foundRowX;
                            }
                            else
                            {
                                // Fallback: find any distinct row below with text in the description X range
                                var belowWords = postSaveWords
                                    .Where(w => (w.y + w.h / 2) > foundRowY + 12 &&
                                                (w.y + w.h / 2) <= contentMaxY &&
                                                Math.Abs((w.x + w.w / 2) - foundRowX) < 200) // Near the description column
                                    .OrderBy(w => w.y)
                                    .ToList();

                                if (belowWords.Count > 0)
                                {
                                    int nextLineY = (int)(belowWords[0].y + belowWords[0].h / 2);
                                    Debug.WriteLine($"[CCC-Web] Next line below (fallback) at Y={nextLineY} (gap={nextLineY - foundRowY}px)");
                                    lastEditRowY = nextLineY;
                                    lastEditRowX = foundRowX;
                                }
                                else
                                {
                                    // Last resort — estimated offset
                                    Debug.WriteLine($"[CCC-Web] No line found below Y={foundRowY}, using foundRow + 30");
                                    lastEditRowY = foundRowY + 30;
                                    lastEditRowX = foundRowX;
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[CCC-Web] Could not find description text in post-save OCR, keeping Y={lastEditRowY}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CCC-Web] Post-save OCR scan failed: {ex.Message}");
                    }

                    opSuccess = true;
                    Debug.WriteLine($"[CCC-Web] Op {displayIndex} complete (lastEditRowX={lastEditRowX}, lastEditRowY={lastEditRowY})");
                    break; // exit retry loop — this op succeeded
                    } // end retry loop (opAttempt)

                    if (!opSuccess)
                    {
                        Debug.WriteLine($"[CCC-Web] FAILED op {displayIndex} after {maxOpRetries} attempts, skipping to next op");
                        StatusChanged?.Invoke(this, $"Op {displayIndex}/{totalOps}: Failed after {maxOpRetries} attempts, re-scanning...");

                        // Re-orient from the last successfully added op so the NEXT op
                        // doesn't start from a stale/wrong position.
                        if (lastAddedDescription != null && successCount > 0)
                        {
                            try
                            {
                                SendKeyPress(VK_ESCAPE);
                                await Task.Delay(300, _cts.Token);
                                var recoveryPos = await RecoverPositionFromEstimate(
                                    lastAddedDescription, ops[successCount - 1].OperationType,
                                    contentMinY, contentMaxY);
                                if (recoveryPos != null)
                                {
                                    lastEditRowX = recoveryPos.Value.nextX;
                                    lastEditRowY = recoveryPos.Value.nextY;
                                    Debug.WriteLine($"[CCC-Web] Post-fail recovery: re-oriented to ({lastEditRowX}, {lastEditRowY})");
                                }
                            }
                            catch (Exception rex)
                            {
                                Debug.WriteLine($"[CCC-Web] Post-fail recovery error: {rex.Message}");
                            }
                        }
                    }
                }

                // Report results
                if (successCount == totalOps)
                {
                    StatusChanged?.Invoke(this, $"Done! Inserted {totalOps} operations.");
                    InsertCompleted?.Invoke(this, true);
                }
                else if (successCount > 0)
                {
                    StatusChanged?.Invoke(this, $"Inserted {successCount}/{totalOps} operations.");
                    InsertCompleted?.Invoke(this, true);
                }
                else
                {
                    StatusChanged?.Invoke(this, "No operations inserted.");
                    InsertCompleted?.Invoke(this, false);
                }
            }
            catch (OperationCanceledException)
            {
                if (_userInputDetected)
                {
                    StatusChanged?.Invoke(this, $"Stopped — user input detected. Inserted {successCount}/{totalOps}.");
                }
                else
                {
                    StatusChanged?.Invoke(this, $"Cancelled. Inserted {successCount}/{totalOps}.");
                }
                InsertCompleted?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                Debug.WriteLine($"[CCC-Web] Exception: {ex}");
                InsertCompleted?.Invoke(this, false);
            }
            finally
            {
                RemoveInputHooks();
                _isInserting = false;
            }
        }

        /// <summary>
        /// Cancel ongoing operation
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();
        }

        #region Safety Input Hooks

        /// <summary>
        /// Installs low-level mouse and keyboard hooks to detect user input during automation.
        /// If user clicks, types, or presses Escape, the automation cancels immediately.
        /// </summary>
        private void InstallInputHooks()
        {
            _userInputDetected = false;
            var moduleHandle = GetModuleHandle(null);

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;

            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

            Debug.WriteLine($"[CCC-Web] Safety hooks installed (mouse={_mouseHook != IntPtr.Zero}, kb={_keyboardHook != IntPtr.Zero})");
        }

        /// <summary>
        /// Removes the input hooks
        /// </summary>
        private void RemoveInputHooks()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            _mouseProc = null;
            _keyboardProc = null;
            Debug.WriteLine($"[CCC-Web] Safety hooks removed");
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isInserting && !_automationClicking)
            {
                int msg = (int)wParam;
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    // Check if this is an injected event (from SendInput/mouse_event) — ignore those
                    // MSLLHOOKSTRUCT.flags is at offset 12, LLMHF_INJECTED = 0x01
                    uint flags = (uint)Marshal.ReadInt32(lParam, 12);
                    if ((flags & 0x01) == 0) // NOT injected = real user click
                    {
                        Debug.WriteLine($"[CCC-Web] SAFETY: User mouse click detected — stopping");
                        _userInputDetected = true;
                        _cts?.Cancel();
                    }
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isInserting && (int)wParam == WM_KEYDOWN)
            {
                // Check if injected (KBDLLHOOKSTRUCT.flags at offset 8, LLKHF_INJECTED = 0x10)
                uint flags = (uint)Marshal.ReadInt32(lParam, 8);
                if ((flags & 0x10) == 0) // NOT injected = real user key
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (vkCode == VK_ESCAPE)
                    {
                        Debug.WriteLine($"[CCC-Web] SAFETY: User pressed Escape — stopping");
                        _userInputDetected = true;
                        _cts?.Cancel();
                    }
                }
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Throws OperationCanceledException if user input was detected
        /// </summary>
        private void CheckUserInput()
        {
            if (_userInputDetected || (_cts != null && _cts.Token.IsCancellationRequested))
            {
                throw new OperationCanceledException("User input detected");
            }
        }

        #endregion

        #region OCR Button Finding

        /// <summary>
        /// Checks if the edit row (identified by the description text) is currently visible
        /// on screen. If not, scrolls the browser to bring it back into view.
        /// Returns the live (liveX, liveY) position, or null if the row was already visible
        /// at the expected position (no update needed).
        ///
        /// This is a generic "make sure I can see the thing before I click it" guard.
        /// Called before every field click (Qty, Labor, Paint, OK) so if the user scrolled
        /// at any point, we recover before continuing.
        /// </summary>
        private async Task<(int liveX, int liveY)?> EnsureEditRowVisible(
            string description, int expectedY, IntPtr browserWindow)
        {
            try
            {
                // Pick a search word from the description
                string[] descWords = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string searchWord = descWords.Where(w => w.Length >= 3).FirstOrDefault()
                    ?? (descWords.Length > 0 ? descWords[0] : "");
                if (searchWord.Length < 2) return null;

                // Quick OCR check — is the description visible right now?
                var words = await OcrFullScreen();
                if (browserWindow != IntPtr.Zero)
                {
                    GetWindowRect(browserWindow, out RECT bRect);
                    words = words.Where(w =>
                        w.x + w.w / 2 >= bRect.Left && w.x + w.w / 2 <= bRect.Right &&
                        w.y + w.h / 2 >= bRect.Top && w.y + w.h / 2 <= bRect.Bottom).ToList();
                }

                var match = words
                    .Where(w => w.text.IndexOf(searchWord, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(w => Math.Abs((w.y + w.h / 2) - expectedY))
                    .FirstOrDefault();

                if (match.text != null)
                {
                    int liveY = (int)(match.y + match.h / 2);
                    int liveX = (int)(match.x + match.w / 2);
                    if (Math.Abs(liveY - expectedY) <= 15)
                    {
                        // Still in place — no action needed
                        return null;
                    }
                    // Moved but still visible — return updated position
                    Debug.WriteLine($"[CCC-Web] EnsureVisible: Edit row shifted from Y={expectedY} to Y={liveY}");
                    return (liveX, liveY);
                }

                // Not visible — scroll to find it
                Debug.WriteLine($"[CCC-Web] EnsureVisible: Edit row not on screen (expected Y={expectedY}), scrolling...");
                if (browserWindow == IntPtr.Zero) return null;

                GetWindowRect(browserWindow, out RECT scrollBounds);
                int scrollX = (scrollBounds.Left + scrollBounds.Right) / 2;
                int scrollY = (scrollBounds.Top + scrollBounds.Bottom) / 2;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    CheckUserInput();
                    // Alternate up/down: up first (user likely scrolled down)
                    int dir = (attempt % 2 == 0) ? -2 : 2;
                    ScrollDown(scrollX, scrollY, dir);
                    await Task.Delay(400, _cts!.Token);

                    var scrollWords = await OcrFullScreen();
                    GetWindowRect(browserWindow, out RECT sRect);
                    scrollWords = scrollWords.Where(w =>
                        w.x + w.w / 2 >= sRect.Left && w.x + w.w / 2 <= sRect.Right &&
                        w.y + w.h / 2 >= sRect.Top && w.y + w.h / 2 <= sRect.Bottom).ToList();

                    var reMatch = scrollWords
                        .Where(w => w.text.IndexOf(searchWord, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(w => w.y)
                        .FirstOrDefault();

                    if (reMatch.text != null)
                    {
                        int foundY = (int)(reMatch.y + reMatch.h / 2);
                        int foundX = (int)(reMatch.x + reMatch.w / 2);
                        Debug.WriteLine($"[CCC-Web] EnsureVisible: Found edit row after scroll at Y={foundY}");
                        return (foundX, foundY);
                    }
                }

                Debug.WriteLine($"[CCC-Web] EnsureVisible: Could not find edit row after scrolling");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] EnsureVisible error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Re-orients by scanning the visible estimate to find the last successfully added
        /// operation. Returns the position of the NEXT row below it (where to click for
        /// the next Insert Line). Used for retry recovery and post-failure recovery.
        /// </summary>
        private async Task<(int nextX, int nextY)?> RecoverPositionFromEstimate(
            string lastDescription, string? lastOpType, int contentMinY, int contentMaxY)
        {
            try
            {
                var words = await OcrFullScreen();
                if (_targetBrowserWindow != IntPtr.Zero)
                {
                    GetWindowRect(_targetBrowserWindow, out RECT bRect);
                    words = words.Where(w =>
                        w.x + w.w / 2 >= bRect.Left && w.x + w.w / 2 <= bRect.Right &&
                        w.y + w.h / 2 >= bRect.Top && w.y + w.h / 2 <= bRect.Bottom).ToList();
                }

                // Build search words from description
                string[] descWords = lastDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var searchWords = descWords.Where(w => w.Length >= 2).Take(3).ToArray();
                string primaryWord = searchWords.Length > 0 ? searchWords[0] : "";
                if (primaryWord.Length < 3 && searchWords.Length > 1)
                {
                    primaryWord = searchWords[1];
                    searchWords = searchWords.Skip(1).ToArray();
                }

                if (primaryWord.Length < 2) return null;

                string expectedOpType = lastOpType != null ? GetCccWebOpName(lastOpType) : "";

                // Find candidates matching the primary word in content area
                var candidates = words
                    .Where(w => w.text.IndexOf(primaryWord, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        w.y + w.h / 2 >= contentMinY && w.y + w.h / 2 <= contentMaxY)
                    .ToList();

                // Score candidates
                var scored = new List<(int score, double y, double x)>();
                foreach (var cand in candidates)
                {
                    double candY = cand.y + cand.h / 2;
                    double candX = cand.x + cand.w / 2;
                    int score = 1;

                    for (int sw = 1; sw < searchWords.Length; sw++)
                    {
                        bool hasWord = words.Any(w =>
                            w.text.IndexOf(searchWords[sw], StringComparison.OrdinalIgnoreCase) >= 0 &&
                            Math.Abs((w.y + w.h / 2) - candY) < 15);
                        if (hasWord) score += 2;
                    }

                    if (!string.IsNullOrEmpty(expectedOpType))
                    {
                        bool hasOpType = words.Any(w =>
                            w.text.IndexOf(expectedOpType, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            Math.Abs((w.y + w.h / 2) - candY) < 15 &&
                            (w.x + w.w / 2) < candX);
                        if (hasOpType) score += 5;
                    }

                    scored.Add((score, candY, candX));
                }

                if (scored.Count == 0) return null;

                var best = scored.OrderByDescending(c => c.score).First();
                int foundRowY = (int)best.y;
                int foundRowX = (int)best.x;
                Debug.WriteLine($"[CCC-Web] Recovery OCR: Found last op '{primaryWord}' at ({foundRowX}, {foundRowY}), score={best.score}");

                // Find next op row below
                var opTypeKeywords = new[] { "Replace", "Repair", "R&I", "R&l", "Blend", "Refinish", "Sublet", "PDR", "Align", "Section", "R&i" };
                var nextOpRows = words
                    .Where(w => (w.y + w.h / 2) > foundRowY + 12 &&
                                (w.y + w.h / 2) <= contentMaxY &&
                                opTypeKeywords.Any(k => w.text.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(w => w.y)
                    .ToList();

                if (nextOpRows.Count > 0)
                {
                    int nextY = (int)(nextOpRows[0].y + nextOpRows[0].h / 2);
                    Debug.WriteLine($"[CCC-Web] Recovery: Next op row at Y={nextY}");
                    return (foundRowX, nextY);
                }

                // Fallback: any text below in the description column area
                var belowWords = words
                    .Where(w => (w.y + w.h / 2) > foundRowY + 12 &&
                                (w.y + w.h / 2) <= contentMaxY &&
                                Math.Abs((w.x + w.w / 2) - foundRowX) < 200)
                    .OrderBy(w => w.y)
                    .ToList();

                if (belowWords.Count > 0)
                {
                    int nextY = (int)(belowWords[0].y + belowWords[0].h / 2);
                    return (foundRowX, nextY);
                }

                // Last resort: estimated offset
                return (foundRowX, foundRowY + 30);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] RecoverPositionFromEstimate error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Represents the screen positions of all fields on the CCC Web edit row.
        /// </summary>
        private class EditRowFields
        {
            public System.Drawing.Point? OpType;
            public System.Drawing.Point? Description;
            public System.Drawing.Point? Qty;
            public System.Drawing.Point? Price;
            public System.Drawing.Point? Labor;
            public System.Drawing.Point? Paint;
        }

        /// <summary>
        /// Find text on the action bar row (same Y as Insert Line, within ±25px).
        /// Used to find "Cancel" button to locate OK next to it.
        /// </summary>
        private async Task<System.Drawing.Point?> FindTextOnActionBar(string text, int actionBarY)
        {
            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);

                // Search ALL words but only match near the action bar Y (±40px tolerance)
                foreach (var line in ocrResult.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        double wordCenterY = word.BoundingRect.Y + word.BoundingRect.Height / 2;
                        if (Math.Abs(wordCenterY - actionBarY) <= 40 &&
                            word.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
                        {
                            var pt = new System.Drawing.Point(
                                (int)(word.BoundingRect.X + word.BoundingRect.Width / 2),
                                (int)(word.BoundingRect.Y + word.BoundingRect.Height / 2));
                            Debug.WriteLine($"[CCC-Web] Found '{text}' on action bar at ({pt.X}, {pt.Y})");
                            return pt;
                        }
                    }
                }

                Debug.WriteLine($"[CCC-Web] '{text}' not found on action bar (Y={actionBarY} ±40)");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindTextOnActionBar error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find text on screen and return the TOPMOST match (smallest Y).
        /// This avoids matching text in the diagram panel at the bottom of CCC Web.
        /// Uses first 2 words of the description for matching.
        /// </summary>
        private async Task<System.Drawing.Point?> FindTextTopmostMatch(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                var allWords = await OcrFullScreen();

                // Filter to browser window — prevents matching text in McStud's own UI
                if (_targetBrowserWindow != IntPtr.Zero)
                {
                    GetWindowRect(_targetBrowserWindow, out RECT bRect);
                    allWords = allWords.Where(w =>
                        w.x + w.w / 2 >= bRect.Left && w.x + w.w / 2 <= bRect.Right &&
                        w.y + w.h / 2 >= bRect.Top && w.y + w.h / 2 <= bRect.Bottom).ToList();
                }

                // Use first 2 words for more reliable matching
                string[] searchWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (searchWords.Length == 0) return null;
                string firstWord = searchWords[0];

                // Find ALL matches of the first word, pick the one with smallest Y (topmost)
                System.Drawing.Point? topmostMatch = null;
                double topmostY = double.MaxValue;

                foreach (var word in allWords)
                {
                    if (!word.text.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double wordY = word.y + word.h / 2;
                    double wordX = word.x + word.w / 2;

                    // If we have a second word, verify it follows nearby (confirms it's our description)
                    if (searchWords.Length > 1)
                    {
                        bool hasSecondWord = false;
                        foreach (var w2 in allWords)
                        {
                            if (w2.text.Contains(searchWords[1], StringComparison.OrdinalIgnoreCase) &&
                                Math.Abs((w2.y + w2.h / 2) - wordY) < 15 && // Same row
                                (w2.x + w2.w / 2) > wordX) // To the right
                            {
                                hasSecondWord = true;
                                break;
                            }
                        }
                        if (!hasSecondWord) continue;
                    }

                    if (wordY < topmostY)
                    {
                        topmostY = wordY;
                        topmostMatch = new System.Drawing.Point((int)wordX, (int)wordY);
                    }
                }

                if (topmostMatch != null)
                    Debug.WriteLine($"[CCC-Web] FindTextTopmostMatch: '{firstWord}' topmost at ({topmostMatch.Value.X}, {topmostMatch.Value.Y})");
                else
                    Debug.WriteLine($"[CCC-Web] FindTextTopmostMatch: '{firstWord}' not found on screen");

                return topmostMatch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindTextTopmostMatch error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find text on screen but ONLY match near a specific Y position (within yTolerance px).
        /// This avoids matching text in the diagram panel at the bottom of CCC Web.
        /// </summary>
        private async Task<System.Drawing.Point?> FindTextNearY(string? text, int referenceY, int yTolerance)
        {
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);

                // Use first word of description for matching
                string searchText = text.Length > 20 ? text.Substring(0, 20) : text;
                string[] searchWords = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (searchWords.Length == 0) return null;

                string firstWord = searchWords[0];
                System.Drawing.Point? bestMatch = null;
                double bestDist = double.MaxValue;

                foreach (var line in ocrResult.Lines)
                {
                    if (!line.Text.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var word in line.Words)
                    {
                        if (!word.Text.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                            continue;

                        double wordY = word.BoundingRect.Y + word.BoundingRect.Height / 2;
                        double dist = Math.Abs(wordY - referenceY);

                        // Only match within Y tolerance
                        if (dist <= yTolerance && dist < bestDist)
                        {
                            bestDist = dist;
                            bestMatch = new System.Drawing.Point(
                                (int)(word.BoundingRect.X + word.BoundingRect.Width / 2),
                                (int)wordY);
                        }
                    }
                }

                if (bestMatch != null)
                {
                    Debug.WriteLine($"[CCC-Web] FindTextNearY: Found '{firstWord}' at ({bestMatch.Value.X}, {bestMatch.Value.Y}), dist={bestDist:F0} from refY={referenceY}");
                }
                else
                {
                    Debug.WriteLine($"[CCC-Web] FindTextNearY: '{firstWord}' not found near Y={referenceY} (±{yTolerance})");
                }
                return bestMatch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindTextNearY error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a specific text string on screen (e.g., a description we just typed).
        /// Returns the center position of the first match.
        /// </summary>
        private async Task<System.Drawing.Point?> FindTextOnScreen(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);

                // Search for the text — use first few words for matching (descriptions can be long/truncated)
                string searchText = text.Length > 20 ? text.Substring(0, 20) : text;
                string[] searchWords = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (searchWords.Length == 0) return null;

                string firstWord = searchWords[0];

                foreach (var line in ocrResult.Lines)
                {
                    // Check if this line contains our text
                    if (line.Text.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                    {
                        // Use the center of the line as the position
                        var words = line.Words.ToList();
                        if (words.Count > 0)
                        {
                            // Find the matching word
                            foreach (var word in words)
                            {
                                if (word.Text.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                                {
                                    var rect = word.BoundingRect;
                                    var pt = new System.Drawing.Point(
                                        (int)(rect.X + rect.Width / 2),
                                        (int)(rect.Y + rect.Height / 2));
                                    Debug.WriteLine($"[CCC-Web] Found text '{firstWord}' at ({pt.X}, {pt.Y})");
                                    return pt;
                                }
                            }
                        }
                    }
                }

                Debug.WriteLine($"[CCC-Web] Text '{firstWord}' not found on screen");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindTextOnScreen error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find the "Description" placeholder text on screen — used as the anchor for the edit row.
        /// </summary>
        private async Task<System.Drawing.Point?> FindDescriptionAnchor()
        {
            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);

                foreach (var line in ocrResult.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        if (word.Text.Equals("Description", StringComparison.OrdinalIgnoreCase))
                        {
                            var rect = word.BoundingRect;
                            var pt = new System.Drawing.Point(
                                (int)(rect.X + rect.Width / 2),
                                (int)(rect.Y + rect.Height / 2));
                            Debug.WriteLine($"[CCC-Web] Found Description at ({pt.X}, {pt.Y})");
                            return pt;
                        }
                    }
                }

                Debug.WriteLine($"[CCC-Web] 'Description' not found on screen");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindDescriptionAnchor error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// OCR the screen to find all edit row fields using Y-proximity.
        /// Layout: [Op Type ▼] [Description] [Qty] [gap] [Price] [0] [0]
        ///
        /// Strategy:
        /// 1. Find "Description" anchor word across ALL OCR lines
        /// 2. Collect ALL OCR words near the same Y coordinate (within 40px)
        /// 3. Identify each field from those words by text matching and X position
        /// This handles OCR splitting the edit row into multiple lines.
        /// </summary>
        private async Task<EditRowFields?> FindEditRowFields(int referenceY = 0)
        {
            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);
                Debug.WriteLine($"[CCC-Web] EditRow OCR text: {ocrResult.Text}");

                // Step 1: Collect ALL words with their positions
                var allWords = new List<(string text, double x, double y, double w, double h)>();
                foreach (var line in ocrResult.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        allWords.Add((word.Text, word.BoundingRect.X, word.BoundingRect.Y,
                            word.BoundingRect.Width, word.BoundingRect.Height));
                    }
                }

                // Step 2: Find "Description" anchor — pick the one CLOSEST to referenceY
                // (avoids column header at top vs actual edit row placeholder)
                double descY = -1;
                double descX = -1;
                double bestDistance = double.MaxValue;
                foreach (var w in allWords)
                {
                    if (w.text.Equals("Description", StringComparison.OrdinalIgnoreCase))
                    {
                        double centerY = w.y + w.h / 2;
                        double dist = referenceY > 0 ? Math.Abs(centerY - referenceY) : 0;
                        Debug.WriteLine($"[CCC-Web] Found 'Description' at Y={centerY:F0}, distance from ref={dist:F0}");
                        if (dist < bestDistance)
                        {
                            bestDistance = dist;
                            descY = centerY;
                            descX = w.x + w.w / 2;
                        }
                    }
                }
                if (descY >= 0)
                {
                    Debug.WriteLine($"[CCC-Web] Anchor: Description at ({descX:F0}, {descY:F0}) (closest to refY={referenceY})");
                }

                if (descY < 0)
                {
                    Debug.WriteLine($"[CCC-Web] Could not find 'Description' text on screen");
                    return null;
                }

                // Step 3: Collect all words near the same Y (within 40px) — these are on the edit row
                double yTolerance = 40;
                var rowWords = new List<(string text, double x, double y, double w, double h)>();
                foreach (var w in allWords)
                {
                    double wordCenterY = w.y + w.h / 2;
                    if (Math.Abs(wordCenterY - descY) <= yTolerance)
                    {
                        rowWords.Add(w);
                    }
                }

                // Sort by X position (left to right)
                rowWords.Sort((a, b) => a.x.CompareTo(b.x));
                Debug.WriteLine($"[CCC-Web] Edit row words ({rowWords.Count}): {string.Join(" | ", rowWords.Select(w => $"'{w.text}'@{w.x:F0}"))}");

                var fields = new EditRowFields();
                fields.Description = new System.Drawing.Point((int)descX, (int)descY);

                // Step 4: Find Op Type — look for known op type names (should be leftmost, before Description)
                var opTypeNames = new[] { "Replace", "Repair", "R&I", "Blend", "Refinish", "Sublet", "PDR", "Align", "Section" };
                foreach (var w in rowWords)
                {
                    if (w.x >= descX) break; // Op Type is always LEFT of Description
                    foreach (var opName in opTypeNames)
                    {
                        if (w.text.Equals(opName, StringComparison.OrdinalIgnoreCase) ||
                            (opName == "R&I" && (w.text == "R&I" || w.text == "R&l" || w.text == "R&i")))
                        {
                            fields.OpType = new System.Drawing.Point((int)(w.x + w.w / 2), (int)(w.y + w.h / 2));
                            Debug.WriteLine($"[CCC-Web] Found OpType '{w.text}' at ({fields.OpType.Value.X}, {fields.OpType.Value.Y})");
                            break;
                        }
                    }
                    if (fields.OpType != null) break;
                }

                // Step 5: Find "Qty" and "Price" placeholder text on the edit row.
                // These are greyed-out inline labels at the same Y as Description.
                // Search ALL words but pick the match CLOSEST to Description's Y.
                double bestQtyDist = double.MaxValue;
                double bestPriceDist = double.MaxValue;
                foreach (var w in allWords)
                {
                    double wx = w.x + w.w / 2;
                    double wy = w.y + w.h / 2;
                    double distFromDesc = Math.Abs(wy - descY);

                    if ((w.text.Equals("Qty", StringComparison.OrdinalIgnoreCase) ||
                         w.text.Equals("Quantity", StringComparison.OrdinalIgnoreCase)) &&
                        wx > descX) // Must be to the RIGHT of Description
                    {
                        if (distFromDesc < bestQtyDist)
                        {
                            bestQtyDist = distFromDesc;
                            fields.Qty = new System.Drawing.Point((int)wx, (int)descY);
                            Debug.WriteLine($"[CCC-Web] Found Qty at X={wx:F0} Y={wy:F0} (dist={distFromDesc:F0}), click at ({fields.Qty.Value.X}, {fields.Qty.Value.Y})");
                        }
                    }
                    else if (w.text.Equals("Price", StringComparison.OrdinalIgnoreCase) &&
                             wx > descX)
                    {
                        if (distFromDesc < bestPriceDist)
                        {
                            bestPriceDist = distFromDesc;
                            fields.Price = new System.Drawing.Point((int)wx, (int)descY);
                            Debug.WriteLine($"[CCC-Web] Found Price at X={wx:F0} Y={wy:F0} (dist={distFromDesc:F0}), click at ({fields.Price.Value.X}, {fields.Price.Value.Y})");
                        }
                    }
                }

                // Step 6: Calculate Labor and Refinish positions from Price.
                // Layout: [Op Type] [Description] [Qty] [Price] [0=BodyLabor] [0=RefinishLabor]
                // Don't rely on finding "0" values (other rows' zeros cause confusion).
                // Instead, calculate from Price position — it's always the same relative layout.
                if (fields.Price != null)
                {
                    // Body Labor is ~120px right of Price center
                    fields.Labor = new System.Drawing.Point(fields.Price.Value.X + 120, (int)descY);
                    // Refinish Labor is ~200px right of Price center
                    fields.Paint = new System.Drawing.Point(fields.Price.Value.X + 200, (int)descY);
                    Debug.WriteLine($"[CCC-Web] Calculated from Price: Labor at X={fields.Labor.Value.X}, Refinish at X={fields.Paint.Value.X}");
                }

                // If Qty not found by OCR, calculate from Price position
                if (fields.Qty == null && fields.Price != null)
                {
                    // Qty is always ~115px left of Price
                    fields.Qty = new System.Drawing.Point(fields.Price.Value.X - 115, (int)descY);
                    Debug.WriteLine($"[CCC-Web] Qty not found, calculated from Price: ({fields.Qty.Value.X}, {fields.Qty.Value.Y})");
                }
                if (fields.Labor != null) Debug.WriteLine($"[CCC-Web] Labor at ({fields.Labor.Value.X}, {fields.Labor.Value.Y})");
                if (fields.Paint != null) Debug.WriteLine($"[CCC-Web] Paint at ({fields.Paint.Value.X}, {fields.Paint.Value.Y})");

                // Log what we found vs missed
                Debug.WriteLine($"[CCC-Web] Fields found: OpType={fields.OpType != null}, Desc={fields.Description != null}, " +
                    $"Qty={fields.Qty != null}, Price={fields.Price != null}, Labor={fields.Labor != null}, Paint={fields.Paint != null}");

                return fields;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindEditRowFields error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures the full screen and uses OCR to find a button by its text label.
        /// Returns screen coordinates of the button center.
        /// Falls back to a targeted region if full screen doesn't find it.
        /// </summary>
        private async Task<System.Drawing.Point?> FindButtonByOcr(string buttonText, int clickX, int clickY, bool searchBelow)
        {
            try
            {
                var allWords = await OcrFullScreen();

                // Filter to browser window only — prevents matching UI text in McStud
                if (_targetBrowserWindow != IntPtr.Zero)
                {
                    GetWindowRect(_targetBrowserWindow, out RECT bRect);
                    allWords = allWords.Where(w =>
                        w.x + w.w / 2 >= bRect.Left && w.x + w.w / 2 <= bRect.Right &&
                        w.y + w.h / 2 >= bRect.Top && w.y + w.h / 2 <= bRect.Bottom).ToList();
                }

                // For multi-word buttons like "Insert Line", search for consecutive words
                string[] buttonWords = buttonText.Split(' ');
                System.Drawing.Point? bestMatch = null;
                double bestDist = double.MaxValue;

                for (int i = 0; i < allWords.Count; i++)
                {
                    bool match = true;
                    for (int bw = 0; bw < buttonWords.Length; bw++)
                    {
                        if (i + bw >= allWords.Count ||
                            !allWords[i + bw].text.Equals(buttonWords[bw], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match) continue;

                    // Calculate center of the matched word span
                    var first = allWords[i];
                    var last = allWords[i + buttonWords.Length - 1];
                    double cx = (first.x + last.x + last.w) / 2;
                    double cy = first.y + first.h / 2;

                    // Prefer match closest to clickY (or below if searchBelow)
                    double dist = searchBelow ? (cy >= clickY ? cy - clickY : 9999) : Math.Abs(cy - clickY);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestMatch = new System.Drawing.Point((int)cx, (int)cy);
                    }
                }

                if (bestMatch != null)
                    Debug.WriteLine($"[CCC-Web] OCR found '{buttonText}' at ({bestMatch.Value.X}, {bestMatch.Value.Y})");
                else
                    Debug.WriteLine($"[CCC-Web] OCR: '{buttonText}' not found in browser window");

                return bestMatch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] OCR error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a button by OCR text, but only accept matches within ±maxYDist of referenceY.
        /// This avoids false positives from page headers and other UI elements far from the expected area.
        /// Returns the closest match to referenceY (within tolerance).
        /// </summary>
        private async Task<System.Drawing.Point?> FindButtonByOcrNearY(string buttonText, int referenceY, int maxYDist)
        {
            try
            {
                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
                }

                var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
                if (softwareBitmap == null) return null;

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);

                System.Drawing.Point? bestMatch = null;
                double bestDist = double.MaxValue;

                foreach (var line in ocrResult.Lines)
                {
                    if (!line.Text.Contains(buttonText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var words = line.Words.ToList();
                    for (int w = 0; w < words.Count; w++)
                    {
                        System.Drawing.Point? candidatePoint = null;

                        // Single word match
                        if (words[w].Text.Equals(buttonText, StringComparison.OrdinalIgnoreCase))
                        {
                            var rect = words[w].BoundingRect;
                            candidatePoint = new System.Drawing.Point(
                                (int)(rect.X + rect.Width / 2),
                                (int)(rect.Y + rect.Height / 2));
                        }
                        // Multi-word match (e.g., "Insert Line")
                        else if (buttonText.Contains(' ') && w + 1 < words.Count)
                        {
                            string combined = words[w].Text + " " + words[w + 1].Text;
                            if (combined.Equals(buttonText, StringComparison.OrdinalIgnoreCase))
                            {
                                var rect1 = words[w].BoundingRect;
                                var rect2 = words[w + 1].BoundingRect;
                                candidatePoint = new System.Drawing.Point(
                                    (int)((rect1.X + rect2.X + rect2.Width) / 2),
                                    (int)(rect1.Y + rect1.Height / 2));
                            }
                        }

                        if (candidatePoint != null)
                        {
                            double dist = Math.Abs(candidatePoint.Value.Y - referenceY);
                            if (dist <= maxYDist && dist < bestDist)
                            {
                                bestDist = dist;
                                bestMatch = candidatePoint;
                            }
                        }
                    }
                }

                if (bestMatch != null)
                    Debug.WriteLine($"[CCC-Web] FindByOcrNearY: '{buttonText}' found at ({bestMatch.Value.X}, {bestMatch.Value.Y}), dist={bestDist:F0} from refY={referenceY}");
                else
                    Debug.WriteLine($"[CCC-Web] FindByOcrNearY: '{buttonText}' not found near Y={referenceY} (±{maxYDist})");

                return bestMatch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] FindButtonByOcrNearY error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a System.Drawing.Bitmap to a SoftwareBitmap for Windows OCR.
        /// </summary>
        private async Task<SoftwareBitmap?> ConvertToSoftwareBitmapAsync(Bitmap bitmap)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, ImageFormat.Bmp);
                memoryStream.Position = 0;

                using var randomAccessStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(memoryStream.ToArray());
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }

                randomAccessStream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

                return softwareBitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] SoftwareBitmap conversion failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Maps our operation type to the CCC Web dropdown option name (for OCR matching).
        /// </summary>
        private string GetCccWebOpName(string opType)
        {
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Rpr",      "Repair" },
                { "Repair",   "Repair" },
                { "Replace",  "Replace" },
                { "R&I",      "R&I" },
                { "R+I",      "R&I" },
                { "Blend",    "Blend" },
                { "Mat",      "Refinish" },
                { "Refinish", "Refinish" },
                { "Sublet",   "Sublet" },
                { "PDR",      "PDR" },
                { "Align",    "Align" },
                { "Section",  "Section" },
            };

            if (mapping.TryGetValue(opType, out var name))
                return name;

            Debug.WriteLine($"[CCC-Web] WARNING: Unknown op type '{opType}', using as-is");
            return opType;
        }

        private string GetTypeaheadChars(string opType)
        {
            if (OpTypeMapping.TryGetValue(opType, out var chars))
                return chars;

            Debug.WriteLine($"[CCC-Web] WARNING: Unknown op type '{opType}', using first 3 chars");
            return opType.Length > 3 ? opType.Substring(0, 3) : opType;
        }

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

        /// <summary>
        /// Bring a specific window to the foreground using its handle — NOT Alt+Tab.
        /// Alt+Tab is unreliable because it switches to whatever Windows thinks is "previous",
        /// which may not be the browser. This method uses the actual window handle.
        /// Uses the Alt-key trick to bypass Win11 focus-stealing prevention.
        /// </summary>
        private bool BringBrowserToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            // If the window is minimized, restore it first
            if (IsIconic(hWnd))
            {
                Debug.WriteLine($"[CCC-Web] Window {hWnd} is minimized, restoring...");
                ShowWindow(hWnd, SW_RESTORE);
            }

            // Simulate Alt key press — this tricks Win11 into allowing SetForegroundWindow.
            // Without this, Win11's focus-stealing prevention blocks the call.
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_MENU;
            inputs[0].u.ki.dwFlags = 0;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_MENU;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());

            bool result = SetForegroundWindow(hWnd);
            Debug.WriteLine($"[CCC-Web] BringBrowserToFront({hWnd}): SetForegroundWindow={result}, current={GetForegroundWindow()}");
            return GetForegroundWindow() == hWnd;
        }

        private void SendKeyPress(byte vk)
        {
            var inputs = new INPUT[2];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.dwFlags = 0;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = vk;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        private void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (CharDelay > 0)
            {
                // Type one character at a time with delay
                foreach (char c in text)
                {
                    var inputs = new INPUT[2];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].u.ki.wVk = 0;
                    inputs[0].u.ki.wScan = (ushort)c;
                    inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
                    inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].u.ki.wVk = 0;
                    inputs[1].u.ki.wScan = (ushort)c;
                    inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                    inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

                    SendInput(2, inputs, Marshal.SizeOf<INPUT>());
                    Thread.Sleep(CharDelay);
                }
            }
            else
            {
                // Batch send all characters at once (fastest)
                var inputs = new INPUT[text.Length * 2];
                for (int i = 0; i < text.Length; i++)
                {
                    ushort c = text[i];

                    inputs[i * 2].type = INPUT_KEYBOARD;
                    inputs[i * 2].u.ki.wVk = 0;
                    inputs[i * 2].u.ki.wScan = c;
                    inputs[i * 2].u.ki.dwFlags = KEYEVENTF_UNICODE;
                    inputs[i * 2].u.ki.dwExtraInfo = IntPtr.Zero;

                    inputs[i * 2 + 1].type = INPUT_KEYBOARD;
                    inputs[i * 2 + 1].u.ki.wVk = 0;
                    inputs[i * 2 + 1].u.ki.wScan = c;
                    inputs[i * 2 + 1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                    inputs[i * 2 + 1].u.ki.dwExtraInfo = IntPtr.Zero;
                }
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            }
        }

        private void LeftClick()
        {
            _automationClicking = true;
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            _automationClicking = false;
        }

        /// <summary>
        /// Scroll down at a given position. Used for dropdown scrolling.
        /// </summary>
        private void ScrollDown(int x, int y, int clicks)
        {
            _automationClicking = true;
            SetCursorPos(x, y);
            // Negative delta = scroll down, 120 per click
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)(-120 * clicks), UIntPtr.Zero);
            _automationClicking = false;
        }

        /// <summary>
        /// OCR the full screen and return all words with their positions.
        /// Reusable helper for any OCR scan.
        /// </summary>
        private async Task<List<(string text, double x, double y, double w, double h)>> OcrFullScreen()
        {
            var result = new List<(string text, double x, double y, double w, double h)>();
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(0, 0, 0, 0, new Size(screenBounds.Width, screenBounds.Height));
            }

            var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
            if (softwareBitmap == null) return result;

            var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);
            foreach (var line in ocrResult.Lines)
            {
                foreach (var word in line.Words)
                {
                    result.Add((word.Text, word.BoundingRect.X, word.BoundingRect.Y,
                        word.BoundingRect.Width, word.BoundingRect.Height));
                }
            }
            return result;
        }

        /// <summary>
        /// OCR the Extended Price popup and fill Qty + Price by clicking directly.
        /// Returns true if popup was found and filled, false if popup not detected.
        /// </summary>
        private async Task<bool> FillExtendedPricePopup(string qtyText, string priceText, int editRowY)
        {
            try
            {
                Debug.WriteLine($"[CCC-Web] Popup: OCR to find Extended Price popup (editRowY={editRowY})...");
                var allWords = await OcrFullScreen();

                // Find "Extended" to confirm popup is open — must be BELOW the edit row
                (string text, double x, double y, double w, double h) extendedWord = default;
                foreach (var word in allWords)
                {
                    if (word.text.Equals("Extended", StringComparison.OrdinalIgnoreCase) &&
                        (word.y + word.h / 2) > editRowY + 30)
                    {
                        extendedWord = word;
                        break;
                    }
                }

                if (extendedWord == default)
                {
                    Debug.WriteLine("[CCC-Web] Popup: 'Extended' not found below edit row — popup may not be open");
                    return false;
                }

                double popupTitleY = extendedWord.y + extendedWord.h / 2;
                Debug.WriteLine($"[CCC-Web] Popup: Found 'Extended' at Y={popupTitleY:F0}");

                // Type Qty value (Qty field is already focused and selected in popup)
                Debug.WriteLine($"[CCC-Web] Popup: Typing Qty='{qtyText}'");
                SelectAllAndType(qtyText);
                await Task.Delay(150, _cts!.Token);

                // Find the STANDALONE "Price" label in the popup (not part of "Extended Price").
                // Strategy: find all "Price" below edit row, then exclude any that have
                // "Extended" immediately to their left (within 150px, same Y ±20px).
                var allPriceBelow = new List<(string text, double x, double y, double w, double h)>();
                foreach (var word in allWords)
                {
                    if (word.text.Equals("Price", StringComparison.OrdinalIgnoreCase))
                    {
                        double wy = word.y + word.h / 2;
                        if (wy > editRowY + 30)
                        {
                            allPriceBelow.Add(word);
                            Debug.WriteLine($"[CCC-Web] Popup: 'Price' candidate at X={word.x:F0}, Y={wy:F0}");
                        }
                    }
                }

                // Filter out any "Price" that is part of "Extended Price"
                var standalonePrices = new List<(string text, double x, double y, double w, double h)>();
                foreach (var pw in allPriceBelow)
                {
                    double pwCenterY = pw.y + pw.h / 2;
                    bool hasExtendedBefore = false;
                    foreach (var ew in allWords)
                    {
                        if (ew.text.Equals("Extended", StringComparison.OrdinalIgnoreCase))
                        {
                            double ewCenterY = ew.y + ew.h / 2;
                            double ewRightEdge = ew.x + ew.w;
                            // "Extended" must be: same Y (±20px), immediately left of "Price" (within 150px gap)
                            if (Math.Abs(ewCenterY - pwCenterY) < 20 && pw.x > ewRightEdge && (pw.x - ewRightEdge) < 150)
                            {
                                hasExtendedBefore = true;
                                Debug.WriteLine($"[CCC-Web] Popup: Skipping 'Price' at X={pw.x:F0} — part of 'Extended Price'");
                                break;
                            }
                        }
                    }
                    if (!hasExtendedBefore)
                    {
                        standalonePrices.Add(pw);
                    }
                }

                var popupPrice = standalonePrices.Count > 0 ? standalonePrices[0] : default;
                if (popupPrice == default && allPriceBelow.Count > 0)
                {
                    // All "Price" had "Extended" before them — shouldn't happen, but use the last resort
                    Debug.WriteLine("[CCC-Web] Popup: All Price words were Extended Price — falling back to Tab");
                }

                if (popupPrice != default)
                {
                    // Click the Price input field — it's below or right of the "Price" label
                    int priceInputX = (int)(popupPrice.x + popupPrice.w / 2);
                    int priceInputY = (int)(popupPrice.y + popupPrice.h + 12); // Below the label
                    Debug.WriteLine($"[CCC-Web] Popup: Found 'Price' label at ({popupPrice.x + popupPrice.w/2:F0}, {popupPrice.y + popupPrice.h/2:F0}), clicking input at ({priceInputX}, {priceInputY})");
                    SetCursorPos(priceInputX, priceInputY);
                    await Task.Delay(50, _cts.Token);
                    LeftClick();
                    await Task.Delay(100, _cts.Token);
                    TripleClick(); // Select any existing content
                    await Task.Delay(50, _cts.Token);
                    Debug.WriteLine($"[CCC-Web] Popup: Typing Price='{priceText}'");
                    TypeText(priceText);
                    await Task.Delay(150, _cts.Token);
                }
                else
                {
                    // Fallback: Tab from Qty to Price
                    Debug.WriteLine("[CCC-Web] Popup: 'Price' label not found, using Tab fallback");
                    SendKeyPress(VK_TAB);
                    await Task.Delay(200, _cts.Token);
                    Debug.WriteLine($"[CCC-Web] Popup: Typing Price='{priceText}' (via Tab)");
                    SelectAllAndType(priceText);
                    await Task.Delay(150, _cts.Token);
                }

                // Find popup's OK button — "OK" text below the popup title area
                (string text, double x, double y, double w, double h) popupOk = default;
                foreach (var word in allWords)
                {
                    if (word.text.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        double wy = word.y + word.h / 2;
                        // Must be below the popup fields area (below the title + some offset)
                        if (wy > popupTitleY + 40)
                        {
                            if (popupOk == default || wy < (popupOk.y + popupOk.h / 2))
                            {
                                popupOk = word; // Closest OK below popup content
                            }
                        }
                    }
                }

                if (popupOk != default)
                {
                    int okX = (int)(popupOk.x + popupOk.w / 2);
                    int okY = (int)(popupOk.y + popupOk.h / 2);
                    Debug.WriteLine($"[CCC-Web] Popup: Clicking OK at ({okX}, {okY})");
                    SetCursorPos(okX, okY);
                    await Task.Delay(50, _cts.Token);
                    LeftClick();
                }
                else
                {
                    // Fallback: press Enter to close popup
                    Debug.WriteLine("[CCC-Web] Popup: OK not found, pressing Enter");
                    SendKeyPress(VK_RETURN);
                }

                await Task.Delay(ExpandDelay, _cts.Token);
                Debug.WriteLine("[CCC-Web] Popup: Closed");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CCC-Web] Popup: Error — {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Triple-click to select all text in the current field.
        /// More reliable than Ctrl+A in browser input fields.
        /// </summary>
        private void TripleClick()
        {
            _automationClicking = true;
            for (int i = 0; i < 3; i++)
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(5);
            }
            _automationClicking = false;
        }

        /// <summary>
        /// Select all text in current field (Ctrl+A) then type new value.
        /// Used for fields that already have "0" — selects the 0, then types over it.
        /// </summary>
        private void SelectAllAndType(string text)
        {
            // Ctrl+A to select all
            var inputs = new INPUT[4];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0x11; // VK_CONTROL
            inputs[0].u.ki.dwFlags = 0;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = 0x41; // VK_A
            inputs[1].u.ki.dwFlags = 0;

            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].u.ki.wVk = 0x41;
            inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].u.ki.wVk = 0x11;
            inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput(4, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(20);

            // Now type the new value (replaces selection)
            TypeText(text);
        }

        #endregion
    }
}
