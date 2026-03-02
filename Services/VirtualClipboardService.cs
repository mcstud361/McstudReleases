#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Virtual Clipboard Service - Central hub for tracking operations ready for export
    ///
    /// When operations are "clipped" from any page (SOPList, BodyOps, etc.),
    /// they're registered here so the Export Tab can display accurate summaries.
    ///
    /// This solves the problem of clipboard monitoring not always catching
    /// operations correctly due to format differences.
    /// </summary>
    public class VirtualClipboardService
    {
        #region Singleton

        private static VirtualClipboardService? _instance;
        private static readonly object _lock = new();

        public static VirtualClipboardService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new VirtualClipboardService();
                    }
                }
                return _instance;
            }
        }

        #endregion

        // Events
        public event EventHandler? OperationsChanged;

        // Internal storage
        private List<VirtualClipboardOp> _operations = new();
        private string _source = ""; // Which page/feature added the operations
        private DateTime _lastUpdated = DateTime.MinValue;

        // Excel summary totals (source of truth - not calculated)
        private decimal _excelTotalPrice;
        private decimal _excelTotalLabor;
        private decimal _excelTotalRefinish;
        private bool _hasExcelTotals; // True if we have Excel totals, false if we need to calculate

        /// <summary>
        /// Current operations in the virtual clipboard
        /// </summary>
        public IReadOnlyList<VirtualClipboardOp> Operations => _operations.AsReadOnly();

        /// <summary>
        /// Number of operations currently stored
        /// </summary>
        public int Count => _operations.Count;

        /// <summary>
        /// Source that added the operations (e.g., "SOP List", "Body Operations")
        /// </summary>
        public string Source => _source;

        /// <summary>
        /// When the operations were last updated
        /// </summary>
        public DateTime LastUpdated => _lastUpdated;

        /// <summary>
        /// Total price - uses Excel totals if available, otherwise calculates
        /// </summary>
        public decimal TotalPrice => _hasExcelTotals ? _excelTotalPrice : _operations.Sum(op => op.Price);

        /// <summary>
        /// Total labor hours - uses Excel totals if available, otherwise calculates
        /// </summary>
        public decimal TotalLaborHours => _hasExcelTotals ? _excelTotalLabor : _operations.Sum(op => op.LaborHours);

        /// <summary>
        /// Total refinish/paint hours - uses Excel totals if available, otherwise calculates
        /// </summary>
        public decimal TotalRefinishHours => _hasExcelTotals ? _excelTotalRefinish : _operations.Sum(op => op.RefinishHours);

        /// <summary>
        /// Whether we have Excel-provided totals (true) or are calculating (false)
        /// </summary>
        public bool HasExcelTotals => _hasExcelTotals;

        /// <summary>
        /// Set operations from McstudDesktop.Models.Operation list (used by SOPListPage, etc.)
        /// </summary>
        public void SetOperations(List<McstudDesktop.Models.Operation> operations, string source)
        {
            _operations.Clear();
            _hasExcelTotals = false; // No Excel totals provided
            foreach (var op in operations)
            {
                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = GetOperationTypeString(op),
                    Description = op.Description,
                    Quantity = op.Quantity,
                    Price = op.Price,
                    LaborHours = op.LaborHours,
                    RefinishHours = op.RefinishHours,
                    Category = op.Category ?? ""
                });
            }
            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set operations with Excel summary totals (most accurate)
        /// </summary>
        public void SetOperationsWithTotals(
            List<McstudDesktop.Models.Operation> operations,
            string source,
            decimal excelTotalPrice,
            decimal excelTotalLabor,
            decimal excelTotalRefinish)
        {
            _operations.Clear();
            foreach (var op in operations)
            {
                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = GetOperationTypeString(op),
                    Description = op.Description,
                    Quantity = op.Quantity,
                    Price = op.Price,
                    LaborHours = op.LaborHours,
                    RefinishHours = op.RefinishHours,
                    Category = op.Category ?? ""
                });
            }

            // Use Excel's totals as source of truth
            _excelTotalPrice = excelTotalPrice;
            _excelTotalLabor = excelTotalLabor;
            _excelTotalRefinish = excelTotalRefinish;
            _hasExcelTotals = true;

            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set operations from OperationRow list
        /// </summary>
        public void SetOperations(List<OperationRow> operations, string source)
        {
            _operations.Clear();
            _hasExcelTotals = false; // No Excel totals provided
            foreach (var op in operations)
            {
                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = op.OperationType,
                    Description = op.Name,
                    Quantity = op.Quantity,
                    Price = (decimal)op.Price,
                    LaborHours = (decimal)op.Labor,
                    RefinishHours = (decimal)op.Refinish,
                    Category = op.Category ?? ""
                });
            }
            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set operations from OperationRow list with Excel summary totals (most accurate)
        /// </summary>
        public void SetOperationsWithTotals(
            List<OperationRow> operations,
            string source,
            decimal excelTotalPrice,
            decimal excelTotalLabor,
            decimal excelTotalRefinish)
        {
            _operations.Clear();
            foreach (var op in operations)
            {
                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = op.OperationType,
                    Description = op.Name,
                    Quantity = op.Quantity,
                    Price = (decimal)op.Price,
                    LaborHours = (decimal)op.Labor,
                    RefinishHours = (decimal)op.Refinish,
                    Category = op.Category ?? ""
                });
            }

            // Use Excel's totals as source of truth
            _excelTotalPrice = excelTotalPrice;
            _excelTotalLabor = excelTotalLabor;
            _excelTotalRefinish = excelTotalRefinish;
            _hasExcelTotals = true;

            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set operations from SmartExportOp list
        /// </summary>
        public void SetOperations(List<SmartExportOp> operations, string source)
        {
            _operations.Clear();
            _hasExcelTotals = false; // No Excel totals provided
            foreach (var op in operations)
            {
                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = op.OperationType,
                    Description = op.Description,
                    Quantity = op.Quantity,
                    Price = op.Price,
                    LaborHours = op.LaborHours,
                    RefinishHours = op.RefinishHours,
                    Category = op.Category
                });
            }
            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set operations from parsed clipboard operations
        /// </summary>
        public void SetOperations(List<ParsedOperation> operations, string source)
        {
            _operations.Clear();
            _hasExcelTotals = false; // No Excel totals provided - clipboard parsing
            foreach (var op in operations)
            {
                decimal.TryParse(op.Price, out var price);
                decimal.TryParse(op.Labor, out var labor);
                decimal.TryParse(op.Paint, out var refinish);
                int.TryParse(op.Qty, out var qty);
                if (qty == 0) qty = 1;

                _operations.Add(new VirtualClipboardOp
                {
                    OperationType = op.Operation,
                    Description = op.Description,
                    Quantity = qty,
                    Price = price,
                    LaborHours = labor,
                    RefinishHours = refinish,
                    Category = ""
                });
            }
            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Add a single operation
        /// </summary>
        public void AddOperation(VirtualClipboardOp operation, string source)
        {
            _operations.Add(operation);
            _hasExcelTotals = false; // Adding individual ops invalidates Excel totals
            _source = source;
            _lastUpdated = DateTime.Now;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clear all operations
        /// </summary>
        public void Clear()
        {
            _operations.Clear();
            _source = "";
            _lastUpdated = DateTime.MinValue;
            _hasExcelTotals = false;
            _excelTotalPrice = 0;
            _excelTotalLabor = 0;
            _excelTotalRefinish = 0;
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Get operations as SmartExportOp for export
        /// </summary>
        public List<SmartExportOp> ToSmartExportOps()
        {
            return _operations.Select(op => new SmartExportOp
            {
                OperationType = op.OperationType,
                Description = op.Description,
                Quantity = op.Quantity,
                Price = op.Price,
                LaborHours = op.LaborHours,
                RefinishHours = op.RefinishHours,
                Category = op.Category
            }).ToList();
        }

        /// <summary>
        /// Convert Operation to string type
        /// </summary>
        private string GetOperationTypeString(McstudDesktop.Models.Operation op)
        {
            // Check for R&I in description first
            if (op.Description.Contains("R&I") || op.Description.StartsWith("12V Battery"))
            {
                return "R&I";
            }

            return op.OperationType switch
            {
                McstudDesktop.Models.OperationType.Repair => "Rpr",
                McstudDesktop.Models.OperationType.Replace => "Replace",
                McstudDesktop.Models.OperationType.RemoveAndInstall => "R&I",
                McstudDesktop.Models.OperationType.Refinish => "Mat",
                McstudDesktop.Models.OperationType.Blend => "Blend",
                _ => "Rpr"
            };
        }
    }

    /// <summary>
    /// Operation stored in the virtual clipboard
    /// </summary>
    public class VirtualClipboardOp
    {
        public string OperationType { get; set; } = "Rpr";
        public string Description { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public decimal Price { get; set; }
        public decimal LaborHours { get; set; }
        public decimal RefinishHours { get; set; }
        public string Category { get; set; } = "";
    }
}
