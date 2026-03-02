using ClosedXML.Excel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Excel calculation engine service - uses ClosedXML to interact with hidden Excel workbook
    /// Optimized for faster access with sheet caching and batch operations
    /// </summary>
    public class ExcelEngineService : IDisposable
    {
        private XLWorkbook? _workbook;
        private string? _workbookPath;
        private volatile bool _isInitialized;
        private readonly object _lockObject = new();
        private readonly SemaphoreSlim _asyncLock = new(1, 1);
        private Task? _initTask;

        // Cached worksheet references for faster access
        private readonly ConcurrentDictionary<string, IXLWorksheet> _sheetCache = new(StringComparer.Ordinal);

        // Pooled string builders for operations that build strings
        private static readonly ObjectPool<System.Text.StringBuilder> _stringBuilderPool =
            new(() => new System.Text.StringBuilder(256), sb => sb.Clear());

        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initialize the Excel engine by loading the master workbook (synchronous)
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            lock (_lockObject)
            {
                if (_isInitialized)
                    return;

                // Copy master workbook to temp location (prevents corruption)
                _workbookPath = CopyMasterToTemp();

                // Load workbook with ClosedXML
                _workbook = new XLWorkbook(_workbookPath);
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[Excel] Workbook initialized synchronously");
            }
        }

        /// <summary>
        /// Initialize the Excel engine asynchronously (recommended for UI responsiveness)
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await _asyncLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                System.Diagnostics.Debug.WriteLine("[Excel] Starting async initialization...");

                // Copy and load workbook on background thread
                await Task.Run(() =>
                {
                    _workbookPath = CopyMasterToTemp();
                    _workbook = new XLWorkbook(_workbookPath);
                });

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[Excel] Workbook initialized asynchronously");
            }
            finally
            {
                _asyncLock.Release();
            }
        }

        /// <summary>
        /// Start initialization in background (fire and forget, check IsInitialized later)
        /// </summary>
        public void StartInitializationAsync()
        {
            if (_isInitialized || _initTask != null)
                return;

            _initTask = Task.Run(async () =>
            {
                try
                {
                    await InitializeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Excel] Background init failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Wait for initialization to complete (if started with StartInitializationAsync)
        /// </summary>
        public async Task WaitForInitializationAsync()
        {
            if (_initTask != null)
            {
                await _initTask;
            }
            else if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        /// <summary>
        /// Get a cached worksheet reference (faster than repeated Worksheet() calls)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IXLWorksheet GetCachedSheet(string sheetName)
        {
            return _sheetCache.GetOrAdd(sheetName, name => _workbook!.Worksheet(name));
        }

        /// <summary>
        /// Clear the sheet cache (call after workbook reload)
        /// </summary>
        private void ClearSheetCache()
        {
            _sheetCache.Clear();
        }

        /// <summary>
        /// Set an input value in the Excel workbook using a mapping key
        /// </summary>
        /// <param name="mappingKey">The key from ExcelMappings.InputControls</param>
        /// <param name="value">The value to set (string, bool, number, etc.)</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInput(string mappingKey, object value)
        {
            EnsureInitialized();

            if (!ExcelMappings.TryGetInput(mappingKey, out var mapping))
                throw new ArgumentException($"Unknown input mapping key: {mappingKey}");

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(mapping.Sheet);
                var cell = sheet.Cell(mapping.Cell);

                // Convert boolean to Yes/No if needed
                if (value is bool boolValue)
                {
                    cell.Value = boolValue ? "Yes" : "No";
                }
                else
                {
                    cell.Value = value?.ToString() ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Set multiple inputs at once (more efficient than individual calls)
        /// Groups by sheet for optimal performance
        /// </summary>
        public void SetInputs(Dictionary<string, object> inputs)
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                // Group inputs by sheet to minimize sheet lookups
                var inputsBySheet = inputs
                    .Select(kvp => (Key: kvp.Key, Value: kvp.Value, Mapping: ExcelMappings.TryGetInput(kvp.Key, out var m) ? m : default))
                    .Where(x => x.Mapping.Sheet != null)
                    .GroupBy(x => x.Mapping.Sheet);

                foreach (var sheetGroup in inputsBySheet)
                {
                    var sheet = GetCachedSheet(sheetGroup.Key);

                    foreach (var item in sheetGroup)
                    {
                        var cell = sheet.Cell(item.Mapping.Cell);

                        if (item.Value is bool boolValue)
                        {
                            cell.Value = boolValue ? "Yes" : "No";
                        }
                        else
                        {
                            cell.Value = item.Value?.ToString() ?? string.Empty;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Set multiple inputs for a specific sheet (fastest batch operation)
        /// </summary>
        public void SetInputsForSheet(string sheetName, IEnumerable<(string cell, object value)> cellValues)
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(sheetName);

                foreach (var (cellAddress, value) in cellValues)
                {
                    var cell = sheet.Cell(cellAddress);

                    if (value is bool boolValue)
                    {
                        cell.Value = boolValue ? "Yes" : "No";
                    }
                    else
                    {
                        cell.Value = value?.ToString() ?? string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Force Excel to recalculate all formulas
        /// </summary>
        public void Calculate()
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                _workbook!.RecalculateAllFormulas();
            }
        }

        /// <summary>
        /// Get an output value from Excel using a mapping key
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object? GetOutput(string mappingKey)
        {
            EnsureInitialized();

            if (!ExcelMappings.TryGetOutput(mappingKey, out var location))
                throw new ArgumentException($"Unknown output mapping key: {mappingKey}");

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(location.Sheet);
                var cell = sheet.Cell(location.Cell);
                return cell.Value;
            }
        }

        /// <summary>
        /// Get a raw cell value by sheet name and cell address
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object? GetCellValue(string sheetName, string cellAddress)
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(sheetName);
                var cell = sheet.Cell(cellAddress);
                return cell.Value;
            }
        }

        /// <summary>
        /// Get a double value from a cell (useful for numeric outputs)
        /// </summary>
        public double GetCellValueAsDouble(string sheetName, string cellAddress)
        {
            var value = GetCellValue(sheetName, cellAddress);

            if (value == null)
                return 0.0;

            if (value is double dbl)
                return dbl;

            if (double.TryParse(value.ToString(), out var result))
                return result;

            return 0.0;
        }

        /// <summary>
        /// Get a string value from a cell
        /// </summary>
        public string GetCellValueAsString(string sheetName, string cellAddress)
        {
            var value = GetCellValue(sheetName, cellAddress);
            return value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Get all operations from a sheet (reads multiple rows)
        /// Reads columns: M (Operation Type), O (Description), Q (Quantity), R (Price), V (Labor), W (Category), X (Refinish)
        /// Optimized to pre-allocate list capacity and minimize string allocations
        /// </summary>
        /// <param name="sheetName">Sheet name</param>
        /// <param name="startRow">First row to read</param>
        /// <param name="endRow">Last row to read</param>
        /// <param name="descriptionColumn">Column letter for description (e.g., "O")</param>
        /// <param name="laborColumn">Column letter for labor hours (e.g., "V")</param>
        /// <param name="priceColumn">Column letter for price (e.g., "R")</param>
        public List<OperationRow> GetOperations(
            string sheetName,
            int startRow,
            int endRow,
            string descriptionColumn = "O",
            string laborColumn = "V",
            string priceColumn = "R")
        {
            EnsureInitialized();

            // Pre-allocate with estimated capacity (assume ~30% of rows have data)
            var estimatedCapacity = Math.Max(16, (endRow - startRow + 1) / 3);
            var operations = new List<OperationRow>(estimatedCapacity);

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(sheetName);

                // Cache column references for faster access
                for (int row = startRow; row <= endRow; row++)
                {
                    var descriptionCell = sheet.Cell(row, GetColumnNumber(descriptionColumn));
                    var description = descriptionCell.GetString();

                    // Skip empty rows
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    // Skip unwanted rows (using cached checks)
                    if (ShouldSkipOperation(description))
                        continue;

                    var opTypeCell = sheet.Cell(row, 13); // Column M = 13
                    var qtyCell = sheet.Cell(row, 17);    // Column Q = 17
                    var priceCell = sheet.Cell(row, GetColumnNumber(priceColumn));
                    var laborCell = sheet.Cell(row, GetColumnNumber(laborColumn));
                    var categoryCell = sheet.Cell(row, 23); // Column W = 23
                    var refinishCell = sheet.Cell(row, 24); // Column X = 24

                    var operation = new OperationRow
                    {
                        OperationType = opTypeCell.GetString(),
                        Name = description,
                        Quantity = (int)GetNumericValue(qtyCell),
                        Price = GetNumericValue(priceCell),
                        Labor = GetNumericValue(laborCell),
                        Category = categoryCell.GetString(),
                        Refinish = GetNumericValue(refinishCell),
                        RowNumber = row
                    };

                    // Only add operations that have actual data (operation type or non-zero values)
                    if (!string.IsNullOrWhiteSpace(operation.OperationType) ||
                        operation.Price > 0 || operation.Labor > 0 || operation.Refinish > 0)
                    {
                        operations.Add(operation);
                    }
                }
            }

            return operations;
        }

        /// <summary>
        /// Convert column letter to column number (cached for common columns)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetColumnNumber(string column)
        {
            return column.Length == 1
                ? column[0] - 'A' + 1
                : (column[0] - 'A' + 1) * 26 + (column[1] - 'A' + 1);
        }

        // Cached skip patterns for faster checking
        private static readonly string[] SkipPatterns = ["#NAME?", "#REF!", "#VALUE!", "#N/A"];

        /// <summary>
        /// Determines if an operation row should be skipped (not displayed)
        /// Optimized with early exits and reduced string allocations
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldSkipOperation(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            // Quick length check - most valid operations are longer
            if (name.Length < 3)
                return true;

            // Check first character for common skip cases
            var firstChar = name[0];
            if (firstChar == '#') // Formula errors
            {
                foreach (var pattern in SkipPatterns)
                {
                    if (name.StartsWith(pattern, StringComparison.Ordinal))
                        return true;
                }
            }

            // Skip link emoji rows (check Unicode range)
            if (firstChar >= 0x1F300) // Emoji range
                return true;

            // Skip "Back to top" links (case-insensitive)
            if (name.Contains("back to top", StringComparison.OrdinalIgnoreCase))
                return true;

            // Skip stats rows (contain "Ops |" pattern)
            if (name.Contains(" ops", StringComparison.OrdinalIgnoreCase) &&
                name.Contains('|'))
                return true;

            // Skip summary stats rows
            if (name.Contains("Ops", StringComparison.Ordinal) &&
                name.Contains("Labor", StringComparison.Ordinal) &&
                name.Contains("Refinish", StringComparison.Ordinal))
                return true;

            return false;
        }

        /// <summary>
        /// Get SOP List summary totals (optimized with cached sheet reference)
        /// </summary>
        public SOPListSummary GetSOPListSummary()
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(ExcelMappings.Sheets.SOPList);

                return new SOPListSummary
                {
                    TotalOperations = (int)GetNumericValue(sheet.Cell(27, 17)), // Q27
                    TotalPrice = GetNumericValue(sheet.Cell(27, 18)),           // R27
                    TotalLabor = GetNumericValue(sheet.Cell(27, 22)),           // V27
                    TotalRefinish = GetNumericValue(sheet.Cell(27, 24)),        // X27
                    SummaryText = sheet.Cell(26, 15).GetString()                // O26
                };
            }
        }

        /// <summary>
        /// Get summary totals for any sheet (reads row 27)
        /// </summary>
        public SheetSummary GetSheetSummary(string sheetName)
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                var sheet = GetCachedSheet(sheetName);

                return new SheetSummary
                {
                    TotalOperations = (int)GetNumericValue(sheet.Cell(27, 17)), // Q27
                    TotalPrice = GetNumericValue(sheet.Cell(27, 18)),           // R27
                    TotalLabor = GetNumericValue(sheet.Cell(27, 22)),           // V27
                    TotalRefinish = GetNumericValue(sheet.Cell(27, 24))         // X27
                };
            }
        }

        /// <summary>
        /// Save the current workbook state (for debugging or backup)
        /// </summary>
        public void SaveWorkbook(string filePath)
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                _workbook!.SaveAs(filePath);
            }
        }

        /// <summary>
        /// Reset all inputs to default values (optimized with batch sheet access)
        /// </summary>
        public void ResetToDefaults()
        {
            EnsureInitialized();

            lock (_lockObject)
            {
                // Group by sheet to minimize sheet lookups
                foreach (var sheetGroup in ExcelMappings.InputsBySheet)
                {
                    var sheet = GetCachedSheet(sheetGroup.Key);

                    foreach (var mapping in sheetGroup.Value)
                    {
                        sheet.Cell(mapping.Cell).Value = mapping.DefaultValue;
                    }
                }
            }

            Calculate();
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized || _workbook == null)
                throw new InvalidOperationException("ExcelEngineService not initialized. Call Initialize() first.");
        }

        private string CopyMasterToTemp()
        {
            // Look for the master workbook in multiple locations
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "MasterWorkbook.xlsx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Unlocked Mcstud Estimating Tool Master.xlsx"),
                @"C:\Users\mcnee\OneDrive\Remote Estimating\App\2.0\Unlocked Mcstud Estimating Tool Master.xlsx"
            };

            string? masterPath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    masterPath = path;
                    break;
                }
            }

            if (masterPath == null)
                throw new FileNotFoundException("Master workbook not found. Searched locations: " + string.Join(", ", possiblePaths));

            // Copy to temp with unique name
            var tempPath = Path.Combine(Path.GetTempPath(), $"MET_{Guid.NewGuid()}.xlsx");
            File.Copy(masterPath, tempPath, true);
            return tempPath;
        }

        private double GetNumericValue(IXLCell cell)
        {
            if (cell.Value.IsNumber)
                return cell.Value.GetNumber();

            if (cell.Value.IsText && double.TryParse(cell.Value.GetText(), out var result))
                return result;

            return 0.0;
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                ClearSheetCache();
                _workbook?.Dispose();
                _workbook = null;

                if (_workbookPath != null && File.Exists(_workbookPath))
                {
                    try
                    {
                        File.Delete(_workbookPath);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }

                _isInitialized = false;
            }
            _asyncLock.Dispose();
        }
    }

    /// <summary>
    /// Simple object pool for reducing allocations
    /// </summary>
    internal sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;
        private readonly ConcurrentBag<T> _pool = new();
        private readonly int _maxSize;

        public ObjectPool(Func<T> factory, Action<T>? reset = null, int maxSize = 16)
        {
            _factory = factory;
            _reset = reset;
            _maxSize = maxSize;
        }

        public T Rent()
        {
            if (_pool.TryTake(out var item))
                return item;
            return _factory();
        }

        public void Return(T item)
        {
            _reset?.Invoke(item);
            if (_pool.Count < _maxSize)
                _pool.Add(item);
        }
    }

    /// <summary>
    /// Represents a single operation row from the Excel workbook
    /// </summary>
    public class OperationRow
    {
        public string OperationType { get; set; } = string.Empty;  // Column M (Rpr, Replace, etc.)
        public string Name { get; set; } = string.Empty;           // Column O (Description)
        public int Quantity { get; set; }                          // Column Q
        public double Price { get; set; }                          // Column R
        public double Labor { get; set; }                          // Column V
        public string Category { get; set; } = string.Empty;       // Column W
        public double Refinish { get; set; }                       // Column X
        public int RowNumber { get; set; }
    }

    /// <summary>
    /// SOP List summary data
    /// </summary>
    public class SOPListSummary
    {
        public int TotalOperations { get; set; }
        public double TotalPrice { get; set; }
        public double TotalLabor { get; set; }
        public double TotalRefinish { get; set; }
        public string SummaryText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generic sheet summary totals (from Excel row 27)
    /// </summary>
    public class SheetSummary
    {
        public int TotalOperations { get; set; }
        public double TotalPrice { get; set; }
        public double TotalLabor { get; set; }
        public double TotalRefinish { get; set; }
    }
}
