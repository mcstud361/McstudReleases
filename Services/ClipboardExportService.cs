#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Handles clipboard-based export to CCC/Mitchell
    ///
    /// Flow:
    /// 1. User copies operations from Excel (Ctrl+C) - tab-separated rows
    /// 2. This service reads and parses the clipboard
    /// 3. User clicks export button
    /// 4. Service finds CCC window, activates it, then pastes
    ///
    /// For CCC Desktop: Paste field by field with Tab between, Enter for next row
    /// </summary>
    public class ClipboardExportService
    {
        // Windows API for finding and activating windows
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        // Timing settings (milliseconds)
        private int _pasteDelay = 30;      // Delay after paste
        private int _enterDelay = 50;      // Delay after Enter before next row
        private int _initialDelay = 500;   // Brief delay after activating window

        /// <summary>
        /// Parsed operations from clipboard
        /// </summary>
        public List<string> Operations { get; private set; } = new List<string>();

        /// <summary>
        /// Raw clipboard text
        /// </summary>
        public string RawClipboardText { get; private set; } = "";

        /// <summary>
        /// Number of operations parsed
        /// </summary>
        public int OperationCount => Operations.Count;

        /// <summary>
        /// Set timing delays
        /// </summary>
        public void SetDelays(int pasteDelayMs = 50, int enterDelayMs = 100, int initialDelayMs = 2000)
        {
            _pasteDelay = pasteDelayMs;
            _enterDelay = enterDelayMs;
            _initialDelay = initialDelayMs;
        }

        /// <summary>
        /// Find CCC ONE Desktop window by searching for windows with "CCC" in the title
        /// </summary>
        private IntPtr FindCCCWindow()
        {
            IntPtr foundWindow = IntPtr.Zero;
            var windowTitles = new List<string>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true; // Continue enumeration

                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();

                if (!string.IsNullOrEmpty(title))
                {
                    windowTitles.Add(title);

                    // Look for CCC ONE windows - common titles include:
                    // "CCC ONE", "CCC Estimating", "CCC ONE Estimating"
                    if (title.Contains("CCC", StringComparison.OrdinalIgnoreCase) &&
                        !title.Contains("McStud", StringComparison.OrdinalIgnoreCase))
                    {
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);

            System.Diagnostics.Debug.WriteLine($"[CCC Search] Found windows: {string.Join(", ", windowTitles.Take(10))}");
            System.Diagnostics.Debug.WriteLine($"[CCC Search] CCC Window handle: {foundWindow}");

            return foundWindow;
        }

        /// <summary>
        /// Activate (bring to foreground) the CCC window
        /// Returns true if successful
        /// </summary>
        private bool ActivateCCCWindow()
        {
            var cccWindow = FindCCCWindow();
            if (cccWindow == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[CCC] Could not find CCC window");
                return false;
            }

            // Restore if minimized, then bring to foreground
            ShowWindow(cccWindow, SW_RESTORE);
            Thread.Sleep(100);
            bool result = SetForegroundWindow(cccWindow);
            Thread.Sleep(200); // Give it time to activate

            System.Diagnostics.Debug.WriteLine($"[CCC] SetForegroundWindow result: {result}");
            return result;
        }

        /// <summary>
        /// Read and parse clipboard contents from Excel
        /// Returns number of operations found
        /// IMPORTANT: Uses UnicodeText format to preserve tab characters!
        /// </summary>
        public int ReadFromClipboard()
        {
            Operations.Clear();
            RawClipboardText = "";

            try
            {
                // Run clipboard read on STA thread
                string? clipboardText = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                        {
                            // Use UnicodeText format to preserve tabs!
                            clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
                        }
                        else if (Clipboard.ContainsText())
                        {
                            clipboardText = Clipboard.GetText();
                        }
                    }
                    catch { }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join(1000); // Wait up to 1 second

                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    return 0;
                }

                RawClipboardText = clipboardText;

                // Debug: Log tab count
                int tabCount = clipboardText.Count(c => c == '\t');
                System.Diagnostics.Debug.WriteLine($"[ClipboardExport] Read clipboard: {clipboardText.Length} chars, {tabCount} tabs");

                // Split by newlines to get rows
                var lines = RawClipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    // DON'T trim - preserve exact formatting including leading/trailing tabs!
                    if (!string.IsNullOrEmpty(line))
                    {
                        // Check if line has actual data (not just zeros/empty)
                        if (HasActualData(line))
                        {
                            Operations.Add(line);
                            int lineTabCount = line.Count(c => c == '\t');
                            System.Diagnostics.Debug.WriteLine($"[ClipboardExport] Added row with {lineTabCount} tabs: {line.Substring(0, Math.Min(50, line.Length))}...");
                        }
                    }
                }

                return Operations.Count;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard read error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if a row has actual data (not just zeros and tabs)
        /// </summary>
        private bool HasActualData(string line)
        {
            var parts = line.Split('\t');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                // If any cell is not empty and not "0", it has real data
                if (!string.IsNullOrEmpty(trimmed) && trimmed != "0")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Parse a raw Excel row into a clean CCC operation
        /// Extracts: Operation, Description, Qty, Price, Labor, Paint
        ///
        /// Your Excel format (tab-separated, 0 = empty):
        /// 0  0  0  0  0  0  Rpr  0  RT and LT Trough Corrosion Protection  0  1  0  0  0  0  0  0  0.3
        ///
        /// Structure:
        /// - Leading zeros (ignore)
        /// - Operation type (Rpr, Replace, etc.)
        /// - Zero
        /// - Description (text)
        /// - Zero
        /// - Qty (usually 1)
        /// - Multiple zeros
        /// - Labor hours (decimal at end, e.g., 0.3)
        /// </summary>
        private ParsedOperation? ParseExcelRow(string rawLine)
        {
            var parts = rawLine.Split('\t');

            // Known operation types
            var opTypes = new[] { "Rpr", "Replace", "R&I", "R+I", "Blend", "Refinish", "O/H", "Sublet", "Add", "Remove", "Install", "Repair" };

            // Step 1: Find the operation type column index
            int opIndex = -1;
            string operation = "";
            for (int i = 0; i < parts.Length; i++)
            {
                var val = parts[i].Trim();
                foreach (var op in opTypes)
                {
                    if (val.Equals(op, StringComparison.OrdinalIgnoreCase))
                    {
                        opIndex = i;
                        operation = val;
                        break;
                    }
                }
                if (opIndex >= 0) break;
            }

            // If no operation type found, skip this row
            if (opIndex < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Parse] No operation type found in: {rawLine.Substring(0, Math.Min(50, rawLine.Length))}...");
                return null;
            }

            // Step 2: Find description - first meaningful text after operation type
            string description = "";
            int descIndex = -1;
            for (int i = opIndex + 1; i < parts.Length; i++)
            {
                var val = parts[i].Trim();
                if (string.IsNullOrEmpty(val) || val == "0") continue;
                if (decimal.TryParse(val, out _)) continue; // Skip numbers
                // Found text - this is the description
                if (val.Length > 2) // Must be at least 3 chars
                {
                    description = val;
                    descIndex = i;
                    break;
                }
            }

            if (string.IsNullOrEmpty(description))
            {
                System.Diagnostics.Debug.WriteLine($"[Parse] No description found for operation: {operation}");
                return null;
            }

            // Step 3: Collect ALL non-zero numbers AFTER the description
            var numbersAfterDesc = new List<decimal>();
            for (int i = descIndex + 1; i < parts.Length; i++)
            {
                var val = parts[i].Trim();
                if (decimal.TryParse(val, out decimal num) && num != 0)
                {
                    numbersAfterDesc.Add(num);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Parse] Numbers after description: [{string.Join(", ", numbersAfterDesc)}]");

            // Step 4: Assign values based on position/characteristics
            // Expected order from your data: Qty (first), then zeros, then Labor (last decimal)
            string qty = "1";
            string price = "";
            string labor = "";
            string paint = "";

            if (numbersAfterDesc.Count > 0)
            {
                // First number is usually Qty
                var firstNum = numbersAfterDesc[0];
                if (firstNum >= 1 && firstNum <= 100 && firstNum == Math.Floor(firstNum))
                {
                    qty = firstNum.ToString("0");
                }

                // Last number is usually Labor (often a decimal like 0.3, 1.0, etc.)
                var lastNum = numbersAfterDesc[numbersAfterDesc.Count - 1];
                if (lastNum > 0 && lastNum <= 100)
                {
                    // Format as hours - show decimal if present
                    if (lastNum == Math.Floor(lastNum))
                        labor = lastNum.ToString("0");
                    else
                        labor = lastNum.ToString("0.0");
                }

                // If there are numbers in between, look for Price (large number)
                for (int i = 1; i < numbersAfterDesc.Count - 1; i++)
                {
                    var num = numbersAfterDesc[i];
                    if (num > 20 && string.IsNullOrEmpty(price))
                    {
                        price = num.ToString("0.00");
                    }
                    // Second decimal could be Paint
                    else if (num <= 50 && num != Math.Floor(num) && string.IsNullOrEmpty(paint))
                    {
                        paint = num.ToString("0.0");
                    }
                }

                // If only one number found, and it's small with decimal, it's labor
                if (numbersAfterDesc.Count == 1 && firstNum <= 50)
                {
                    qty = "1"; // Reset qty to default
                    if (firstNum == Math.Floor(firstNum))
                        labor = firstNum.ToString("0");
                    else
                        labor = firstNum.ToString("0.0");
                }

                // If first and last are the same (only 2 numbers: qty and labor)
                if (numbersAfterDesc.Count == 2)
                {
                    // First is qty, last is labor
                    var qtyNum = numbersAfterDesc[0];
                    var laborNum = numbersAfterDesc[1];
                    qty = qtyNum.ToString("0");
                    if (laborNum == Math.Floor(laborNum))
                        labor = laborNum.ToString("0");
                    else
                        labor = laborNum.ToString("0.0");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Parse] Parsed: Op={operation}, Desc={description}, Qty={qty}, Price={price}, Labor={labor}, Paint={paint}");

            return new ParsedOperation
            {
                Operation = operation,
                Description = description,
                Qty = qty,
                Price = price,
                Labor = labor,
                Paint = paint
            };
        }

        /// <summary>
        /// Get parsed operations ready for CCC export
        /// </summary>
        public List<ParsedOperation> GetParsedOperations()
        {
            var parsed = new List<ParsedOperation>();
            foreach (var raw in Operations)
            {
                var op = ParseExcelRow(raw);
                if (op != null)
                {
                    parsed.Add(op);
                }
            }
            return parsed;
        }

        /// <summary>
        /// Build a CCC-formatted row from parsed operation
        /// Format: Operation [TAB] Description [TAB] Qty [TAB] Price [TAB] Labor [TAB] Paint
        /// </summary>
        private string BuildCCCRow(ParsedOperation op)
        {
            // CCC expects: Operation, Description, Qty, Price, Labor Hours, Paint Hours
            return string.Join("\t",
                op.Operation,
                op.Description,
                op.Qty,
                op.Price,
                op.Labor,
                op.Paint
            );
        }

        /// <summary>
        /// Paste a single field value (only if not empty)
        /// </summary>
        private void PasteField(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Clipboard.SetText(value);
                Thread.Sleep(5);
                SendKeys.SendWait("^v");
            }
        }

        /// <summary>
        /// Get a preview of operations for display (uses parsed data)
        /// </summary>
        public List<OperationPreview> GetPreview()
        {
            var previews = new List<OperationPreview>();
            var parsedOps = GetParsedOperations();

            foreach (var parsed in parsedOps)
            {
                previews.Add(new OperationPreview
                {
                    Type = parsed.Operation,
                    Description = parsed.Description,
                    Price = parsed.Price,
                    Labor = parsed.Labor,
                    RawLine = BuildCCCRow(parsed)
                });
            }

            return previews;
        }

        /// <summary>
        /// Export to CCC Desktop
        /// Pastes each row into column A, presses Enter to move to next line
        /// </summary>
        public async Task<ExportResult> ExportToCCCDesktopAsync(
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Operations.Count == 0)
            {
                return new ExportResult
                {
                    Success = false,
                    Message = "No operations to export. Copy from Excel first (Ctrl+C)."
                };
            }

            // Parse operations into clean CCC format
            var parsedOps = GetParsedOperations();
            var totalCount = parsedOps.Count;

            if (totalCount == 0)
            {
                return new ExportResult
                {
                    Success = false,
                    Message = "No valid operations found to export."
                };
            }

            // Run on STA thread for clipboard/SendKeys operations
            var tcs = new TaskCompletionSource<ExportResult>();

            var thread = new Thread(() =>
            {
                try
                {
                    // NO window activation - user should already be focused on CCC
                    // where they want to paste. F9 triggers paste at their cursor position.
                    progress?.Report(new ExportProgress
                    {
                        Message = "Exporting to CCC...",
                        Current = 0,
                        Total = totalCount
                    });

                    // Small delay to let user release F9 key
                    Thread.Sleep(100);

                    int exported = 0;

                    foreach (var parsedOp in parsedOps)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.SetResult(new ExportResult
                            {
                                Success = false,
                                Message = $"Cancelled after {exported} operations",
                                ExportedCount = exported
                            });
                            return;
                        }

                        // Paste field by field: Operation, Description, Qty, Price, Labor, Paint
                        // Each field: paste value, then Tab to next field

                        // Field 1: Operation (Rpr, Replace, etc.)
                        PasteField(parsedOp.Operation);
                        SendKeys.SendWait("{TAB}");
                        Thread.Sleep(_pasteDelay);

                        // Field 2: Description
                        PasteField(parsedOp.Description);
                        SendKeys.SendWait("{TAB}");
                        Thread.Sleep(_pasteDelay);

                        // Field 3: Qty
                        PasteField(parsedOp.Qty);
                        SendKeys.SendWait("{TAB}");
                        Thread.Sleep(_pasteDelay);

                        // Field 4: Price
                        PasteField(parsedOp.Price);
                        SendKeys.SendWait("{TAB}");
                        Thread.Sleep(_pasteDelay);

                        // Field 5: Labor
                        PasteField(parsedOp.Labor);
                        SendKeys.SendWait("{TAB}");
                        Thread.Sleep(_pasteDelay);

                        // Field 6: Paint
                        PasteField(parsedOp.Paint);

                        // Press Enter to move to next line
                        SendKeys.SendWait("{ENTER}");
                        Thread.Sleep(_enterDelay);

                        exported++;

                        progress?.Report(new ExportProgress
                        {
                            Message = $"Exporting... {exported}/{totalCount}",
                            Current = exported,
                            Total = totalCount
                        });
                    }

                    tcs.SetResult(new ExportResult
                    {
                        Success = true,
                        Message = $"Done! Exported {exported} operations",
                        ExportedCount = exported
                    });
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new ExportResult
                    {
                        Success = false,
                        Message = $"Export error: {ex.Message}"
                    });
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return await tcs.Task;
        }

        /// <summary>
        /// Export to CCC Web (may need different approach)
        /// </summary>
        public async Task<ExportResult> ExportToCCCWebAsync(
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // CCC Web might need different handling
            // For now, try same approach as Desktop
            return await ExportToCCCDesktopAsync(progress, cancellationToken);
        }

        /// <summary>
        /// Export to Mitchell
        /// Mitchell may have different field order/format
        /// </summary>
        public async Task<ExportResult> ExportToMitchellAsync(
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Operations.Count == 0)
            {
                return new ExportResult
                {
                    Success = false,
                    Message = "No operations to export. Copy from Excel first (Ctrl+C)."
                };
            }

            // TODO: Implement Mitchell-specific paste sequence
            // Mitchell may need different column order or key sequence

            return new ExportResult
            {
                Success = false,
                Message = "Mitchell export not yet implemented. Need to know Mitchell's field order."
            };
        }

        /// <summary>
        /// Clear the stored operations
        /// </summary>
        public void Clear()
        {
            Operations.Clear();
            RawClipboardText = "";
        }

        /// <summary>
        /// Get formatted text ready to paste into CCC or Mitchell
        /// Returns tab-separated rows with proper column order
        /// </summary>
        public string GetFormattedText(string format)
        {
            var parsedOps = GetParsedOperations();
            if (parsedOps.Count == 0) return "";

            var lines = new List<string>();

            foreach (var op in parsedOps)
            {
                string line;
                if (format == "Mitchell")
                {
                    // Mitchell format might be different - adjust as needed
                    line = string.Join("\t",
                        op.Operation,
                        op.Description,
                        op.Qty,
                        op.Labor,
                        op.Paint,
                        op.Price
                    );
                }
                else
                {
                    // CCC format: Operation, Description, Qty, Price, Labor, Paint
                    line = string.Join("\t",
                        op.Operation,
                        op.Description,
                        op.Qty,
                        op.Price,
                        op.Labor,
                        op.Paint
                    );
                }
                lines.Add(line);
            }

            return string.Join("\r\n", lines);
        }

        /// <summary>
        /// Format Operation objects for clipboard based on target system
        /// </summary>
        public static string FormatOperationsForClipboard(
            List<McstudDesktop.Models.Operation> operations,
            string targetSystem)
        {
            if (operations == null || operations.Count == 0) return "";

            var lines = new List<string>();

            foreach (var op in operations)
            {
                // Get operation type string
                string opType = op.OperationType switch
                {
                    McstudDesktop.Models.OperationType.Repair => "Rpr",
                    McstudDesktop.Models.OperationType.Replace => "Replace",
                    McstudDesktop.Models.OperationType.RemoveAndInstall => "R&I",
                    McstudDesktop.Models.OperationType.Refinish => "Refinish",
                    McstudDesktop.Models.OperationType.Blend => "Blend",
                    _ => "Rpr"
                };

                // Check for R&I in description
                if (op.Description.Contains("R&I") || op.Description.StartsWith("12V Battery"))
                {
                    opType = "R&I";
                }

                string qty = op.Quantity.ToString();
                string price = op.Price > 0 ? op.Price.ToString("F2") : "0";
                string labor = op.LaborHours > 0 ? op.LaborHours.ToString("F1") : "0";
                string refinish = op.RefinishHours > 0 ? op.RefinishHours.ToString("F1") : "0";

                string line;
                switch (targetSystem)
                {
                    case "CCC Desktop":
                    case "CCC Web":
                        line = $"{opType}\t{op.Description}\t{qty}\t{price}\t{labor}\t{refinish}";
                        break;

                    case "Mitchell":
                        // Mitchell format: Operation, Description, Qty, Labor, Paint, Price
                        line = $"{opType}\t{op.Description}\t{qty}\t{labor}\t{refinish}\t{price}";
                        break;

                    default:
                        // Plain format
                        line = $"{op.Description}\t{labor}\t{refinish}\t{price}";
                        break;
                }
                lines.Add(line);
            }

            return string.Join("\r\n", lines);
        }

        /// <summary>
        /// Copy operations to clipboard for the specified target system
        /// </summary>
        public static void CopyToClipboard(
            List<McstudDesktop.Models.Operation> operations,
            string targetSystem)
        {
            string formatted = FormatOperationsForClipboard(operations, targetSystem);
            if (string.IsNullOrEmpty(formatted)) return;

            CopyTextToClipboard(formatted);
        }

        /// <summary>
        /// Format OperationRow objects for clipboard based on target system
        /// </summary>
        public static string FormatOperationRowsForClipboard(
            List<OperationRow> operations,
            string targetSystem)
        {
            if (operations == null || operations.Count == 0) return "";

            var lines = new List<string>();

            foreach (var op in operations)
            {
                string opType = op.OperationType;
                string qty = op.Quantity.ToString();
                string price = op.Price > 0 ? op.Price.ToString("F2") : "0";
                string labor = op.Labor > 0 ? op.Labor.ToString("F1") : "0";
                string refinish = op.Refinish > 0 ? op.Refinish.ToString("F1") : "0";

                string line;
                switch (targetSystem)
                {
                    case "CCC Desktop":
                    case "CCC Web":
                        line = $"{opType}\t{op.Name}\t{qty}\t{price}\t{labor}\t{refinish}";
                        break;

                    case "Mitchell":
                        line = $"{opType}\t{op.Name}\t{qty}\t{labor}\t{refinish}\t{price}";
                        break;

                    default:
                        line = $"{op.Name}\t{labor}\t{refinish}\t{price}";
                        break;
                }
                lines.Add(line);
            }

            return string.Join("\r\n", lines);
        }

        /// <summary>
        /// Copy OperationRow list to clipboard for the specified target system
        /// </summary>
        public static void CopyToClipboard(
            List<OperationRow> operations,
            string targetSystem)
        {
            string formatted = FormatOperationRowsForClipboard(operations, targetSystem);
            if (string.IsNullOrEmpty(formatted)) return;

            CopyTextToClipboard(formatted);
        }

        /// <summary>
        /// Helper to copy text to clipboard on STA thread
        /// </summary>
        private static void CopyTextToClipboard(string text)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Clipboard] Error copying: {ex.Message}");
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(1000);
        }
    }

    /// <summary>
    /// Preview of a parsed operation
    /// </summary>
    public class OperationPreview
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public string Price { get; set; } = "";
        public string Labor { get; set; } = "";
        public string RawLine { get; set; } = "";
    }

    /// <summary>
    /// Parsed operation with clean CCC fields
    /// </summary>
    public class ParsedOperation
    {
        public string Operation { get; set; } = "";   // Rpr, Replace, R&I, etc.
        public string Description { get; set; } = ""; // Line description
        public string Qty { get; set; } = "1";        // Quantity
        public string Price { get; set; } = "";       // Dollar amount
        public string Labor { get; set; } = "";       // Labor hours
        public string Paint { get; set; } = "";       // Paint/Refinish hours
    }

    /// <summary>
    /// Result of export operation
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int ExportedCount { get; set; }
        public int FailedCount { get; set; }
    }

    /// <summary>
    /// Progress update during export
    /// </summary>
    public class ExportProgress
    {
        public string Message { get; set; } = "";
        public int Current { get; set; }
        public int Total { get; set; }
        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
    }
}
