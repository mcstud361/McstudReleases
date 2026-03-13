#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Bridge between ExcelFormulaService (loads ExcelOperationsDB.json) and the ghost estimate engine.
    /// Indexes all operations across all sheets by normalized description for fast lookups.
    /// Respects user overrides from GhostConfigService.
    /// </summary>
    public class ExcelGhostDataProvider
    {
        private static ExcelGhostDataProvider? _instance;
        public static ExcelGhostDataProvider Instance => _instance ??= new ExcelGhostDataProvider();

        private readonly ExcelFormulaService _excelService;
        private readonly GhostConfigService _ghostConfig;

        // Indexed operations: normalized description -> (sheetName, operation)
        private readonly Dictionary<string, List<(string Sheet, ExcelOperation Op)>> _operationIndex = new(StringComparer.OrdinalIgnoreCase);
        private bool _indexed;

        public ExcelGhostDataProvider()
        {
            _excelService = ExcelFormulaService.Instance;
            _ghostConfig = GhostConfigService.Instance;
            BuildIndex();
        }

        private void BuildIndex()
        {
            if (_indexed) return;

            try
            {
                foreach (var sheetName in _excelService.GetSheetNames())
                {
                    var ops = _excelService.GetOperationsForSheet(sheetName);
                    foreach (var op in ops)
                    {
                        if (string.IsNullOrWhiteSpace(op.Description)) continue;

                        var key = NormalizeDescription(op.Description);
                        if (!_operationIndex.TryGetValue(key, out var list))
                        {
                            list = new List<(string, ExcelOperation)>();
                            _operationIndex[key] = list;
                        }
                        list.Add((sheetName, op));
                    }
                }

                _indexed = true;
                System.Diagnostics.Debug.WriteLine($"[ExcelGhost] Indexed {_operationIndex.Count} unique operations across all sheets");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExcelGhost] Error building index: {ex.Message}");
            }
        }

        /// <summary>
        /// Fuzzy lookup by keyword. Searches description text across all sheets.
        /// Returns matches sorted by relevance (exact > contains > partial word match).
        /// </summary>
        public List<ExcelGhostMatch> FindOperation(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new();

            var keyLower = keyword.ToLowerInvariant().Trim();
            var results = new List<ExcelGhostMatch>();

            foreach (var (normalizedKey, entries) in _operationIndex)
            {
                var keyNorm = normalizedKey.ToLowerInvariant();

                int score = 0;
                if (keyNorm == keyLower)
                    score = 100; // exact
                else if (keyNorm.Contains(keyLower))
                    score = 70; // contains full keyword
                else
                {
                    // Check individual words
                    var words = keyLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var matchedWords = words.Count(w => keyNorm.Contains(w));
                    if (matchedWords > 0)
                        score = 30 + (matchedWords * 20 / words.Length);
                }

                if (score > 0)
                {
                    foreach (var (sheet, op) in entries)
                    {
                        results.Add(new ExcelGhostMatch
                        {
                            SheetName = sheet,
                            Operation = op,
                            RelevanceScore = score,
                            Description = op.Description ?? ""
                        });
                    }
                }
            }

            return results.OrderByDescending(r => r.RelevanceScore).ToList();
        }

        /// <summary>
        /// Get all operations for a specific sheet.
        /// </summary>
        public List<ExcelOperation> GetSheetOperations(string sheetName)
        {
            return _excelService.GetOperationsForSheet(sheetName);
        }

        /// <summary>
        /// Get all available sheet names.
        /// </summary>
        public IEnumerable<string> GetSheetNames()
        {
            return _excelService.GetSheetNames();
        }

        /// <summary>
        /// Get effective labor hours for an operation, respecting user overrides.
        /// </summary>
        public decimal GetEffectiveLaborHours(string description)
        {
            var opOverride = _ghostConfig.GetOperationOverride(NormalizeDescription(description));
            if (opOverride?.LaborHours != null)
                return opOverride.LaborHours.Value;

            var matches = FindOperation(description);
            var best = matches.FirstOrDefault();
            return best?.Operation.LaborHours ?? 0m;
        }

        /// <summary>
        /// Get effective refinish hours for an operation, respecting user overrides.
        /// </summary>
        public decimal GetEffectiveRefinishHours(string description)
        {
            var opOverride = _ghostConfig.GetOperationOverride(NormalizeDescription(description));
            if (opOverride?.RefinishHours != null)
                return opOverride.RefinishHours.Value;

            var matches = FindOperation(description);
            var best = matches.FirstOrDefault();
            return best?.Operation.RefinishHours ?? 0m;
        }

        /// <summary>
        /// Get effective price for an operation, respecting user overrides.
        /// </summary>
        public decimal GetEffectivePrice(string description)
        {
            var opOverride = _ghostConfig.GetOperationOverride(NormalizeDescription(description));
            if (opOverride?.Price != null)
                return opOverride.Price.Value;

            var matches = FindOperation(description);
            var best = matches.FirstOrDefault();
            return best?.Operation.Price ?? 0m;
        }

        /// <summary>
        /// Check if an operation is disabled by user override.
        /// </summary>
        public bool IsOperationDisabled(string description)
        {
            var opOverride = _ghostConfig.GetOperationOverride(NormalizeDescription(description));
            return opOverride?.Disabled == true;
        }

        /// <summary>
        /// Try to find an Excel operation for a given part name and operation type.
        /// Used by ghost engine to insert Excel data in the fallback chain.
        /// </summary>
        public (bool Found, decimal LaborHours, decimal RefinishHours, decimal Price, string SheetName)
            LookupForGhost(string partName, string operationType)
        {
            // Build search queries from part name + operation type
            var queries = new List<string>
            {
                $"{partName} {operationType}",
                partName,
                $"{operationType} {partName}"
            };

            foreach (var query in queries)
            {
                var matches = FindOperation(query);
                if (matches.Count > 0)
                {
                    var best = matches[0];
                    if (best.RelevanceScore >= 50)
                    {
                        return (true, best.Operation.LaborHours, best.Operation.RefinishHours,
                                best.Operation.Price, best.SheetName);
                    }
                }
            }

            return (false, 0, 0, 0, "");
        }

        private static string NormalizeDescription(string description)
        {
            return description.Trim().ToLowerInvariant()
                .Replace("-", " ").Replace("  ", " ");
        }
    }

    public class ExcelGhostMatch
    {
        public string SheetName { get; set; } = "";
        public ExcelOperation Operation { get; set; } = new();
        public int RelevanceScore { get; set; }
        public string Description { get; set; } = "";
    }
}
