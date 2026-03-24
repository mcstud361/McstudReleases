#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Aggregates current estimate state from all sources into a single context snapshot
    /// for use in AI system prompts. Provides a text representation of the full app state.
    /// </summary>
    public class EstimateContextService
    {
        private static EstimateContextService? _instance;
        public static EstimateContextService Instance => _instance ??= new EstimateContextService();

        private List<MustHaveCategory>? _mustHaveCategories;

        public EstimateContextService()
        {
            LoadContextMustHaveOperations();
        }

        /// <summary>
        /// Get the current estimate context snapshot
        /// </summary>
        public EstimateContext GetContext()
        {
            var context = new EstimateContext();

            // Vehicle info from advisor session
            try
            {
                var advisor = EstimateAIAdvisorService.Instance;
                var session = advisor.GetSessionSnapshot();
                context.VehicleInfo = session.VehicleInfo;
                context.InsuranceCompany = session.InsuranceCompany;
                context.EnteredOperations = session.EnteredOperations
                    .Select(op => new ContextOperation
                    {
                        Part = op.Part,
                        OperationType = op.Operation,
                        Hours = op.Hours
                    }).ToList();
            }
            catch { }

            // Screen monitor accumulated operations (the primary data source when estimating)
            try
            {
                var coaching = McstudDesktop.Services.LiveCoachingService.Instance;
                if (coaching.IsRunning)
                {
                    var accumulated = coaching.AccumulatedOperations;
                    if (accumulated.Count > 0)
                    {
                        context.ScreenMonitorOperations = accumulated
                            .Select(op => new ContextOperation
                            {
                                Part = !string.IsNullOrEmpty(op.PartName) ? op.PartName : op.Description,
                                OperationType = op.OperationType,
                                Hours = op.LaborHours + op.RefinishHours,
                                Price = op.Price
                            }).ToList();

                        // Use vehicle info from coaching if not set from session
                        if (string.IsNullOrEmpty(context.VehicleInfo))
                        {
                            var snapshot = coaching.LatestSnapshot;
                            if (snapshot != null && !string.IsNullOrEmpty(snapshot.VehicleInfo))
                                context.VehicleInfo = snapshot.VehicleInfo;
                        }
                    }
                }
            }
            catch { }

            // Current operations from VirtualClipboard
            try
            {
                var clipboard = VirtualClipboardService.Instance;
                if (clipboard.Count > 0)
                {
                    context.ClipboardOperations = clipboard.Operations
                        .Select(op => new ContextOperation
                        {
                            Part = op.Description,
                            OperationType = op.OperationType,
                            Hours = op.LaborHours + op.RefinishHours,
                            Price = op.Price
                        }).ToList();
                    context.ClipboardSource = clipboard.Source;
                }
            }
            catch { }

            // Learned stats
            try
            {
                var kb = LearnedKnowledgeBase.Instance;
                context.TotalEstimatesAnalyzed = kb.TotalEstimatesAnalyzed;
                context.TotalPartsLearned = kb.TotalPartsLearned;
                context.TotalPatternsLearned = kb.TotalPatternsLearned;
            }
            catch { }

            // Must-have operations checklist
            try
            {
                context.MustHaveChecklist = BuildMustHaveChecklist(context);
            }
            catch { }

            return context;
        }

        /// <summary>
        /// Build a text representation of the current context for AI system prompts.
        /// Truncated to a reasonable size to stay within token limits.
        /// </summary>
        public string BuildContextText()
        {
            var context = GetContext();
            var sb = new StringBuilder();

            // Vehicle
            if (!string.IsNullOrEmpty(context.VehicleInfo))
                sb.AppendLine($"Vehicle: {context.VehicleInfo}");

            // Entered operations (session)
            if (context.EnteredOperations.Count > 0)
            {
                sb.AppendLine($"\nOperations entered ({context.EnteredOperations.Count}):");
                foreach (var op in context.EnteredOperations.Take(30))
                {
                    var hours = op.Hours > 0 ? $" {op.Hours:N1}h" : "";
                    var price = op.Price > 0 ? $" ${op.Price:N2}" : "";
                    sb.AppendLine($"  - {op.Part} ({op.OperationType}){hours}{price}");
                }
                if (context.EnteredOperations.Count > 30)
                    sb.AppendLine($"  ... and {context.EnteredOperations.Count - 30} more");
            }

            // Screen monitor operations (primary source when actively estimating)
            if (context.ScreenMonitorOperations.Count > 0)
            {
                sb.AppendLine($"\nScreen monitor operations ({context.ScreenMonitorOperations.Count}):");
                foreach (var op in context.ScreenMonitorOperations.Take(40))
                {
                    var hours = op.Hours > 0 ? $" {op.Hours:N1}h" : "";
                    var price = op.Price > 0 ? $" ${op.Price:N2}" : "";
                    sb.AppendLine($"  - {op.Part} ({op.OperationType}){hours}{price}");
                }
                if (context.ScreenMonitorOperations.Count > 40)
                    sb.AppendLine($"  ... and {context.ScreenMonitorOperations.Count - 40} more");
            }

            // Clipboard operations (if different from entered)
            if (context.ClipboardOperations.Count > 0 && context.EnteredOperations.Count == 0)
            {
                sb.AppendLine($"\nClipboard operations ({context.ClipboardOperations.Count} from {context.ClipboardSource}):");
                foreach (var op in context.ClipboardOperations.Take(20))
                {
                    var hours = op.Hours > 0 ? $" {op.Hours:N1}h" : "";
                    var price = op.Price > 0 ? $" ${op.Price:N2}" : "";
                    sb.AppendLine($"  - {op.Part} ({op.OperationType}){hours}{price}");
                }
            }

            // Must-have checklist
            if (context.MustHaveChecklist.Count > 0)
            {
                var present = context.MustHaveChecklist.Where(m => m.IsPresent).ToList();
                var missing = context.MustHaveChecklist.Where(m => !m.IsPresent).ToList();

                if (missing.Count > 0)
                {
                    sb.AppendLine($"\nMust-have operations MISSING ({missing.Count}):");
                    foreach (var m in missing)
                    {
                        var detail = m.LaborHours > 0 ? $" ({m.OperationType}, {m.LaborHours:N1}h)" : $" ({m.OperationType}, ${m.Price:N2})";
                        sb.AppendLine($"  - {m.Description}{detail} [{m.Category}]");
                    }
                }

                if (present.Count > 0)
                {
                    sb.AppendLine($"\nMust-have operations present ({present.Count}):");
                    foreach (var p in present)
                        sb.AppendLine($"  - {p.Description} [OK]");
                }
            }

            // Learning stats
            if (context.TotalEstimatesAnalyzed > 0)
            {
                sb.AppendLine($"\nLearning database: {context.TotalEstimatesAnalyzed} estimates analyzed, {context.TotalPartsLearned} parts, {context.TotalPatternsLearned} patterns");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get must-have operations data for direct access
        /// </summary>
        public List<MustHaveCategory> GetMustHaveCategories()
        {
            return _mustHaveCategories ?? new List<MustHaveCategory>();
        }

        /// <summary>
        /// Get all must-have operations as a flat list
        /// </summary>
        public List<ContextMustHaveOperation> GetAllContextMustHaveOperations()
        {
            if (_mustHaveCategories == null) return new List<ContextMustHaveOperation>();
            return _mustHaveCategories.SelectMany(c => c.Operations).ToList();
        }

        #region Private

        /// <summary>
        /// Normalize text for matching: lowercase, replace & with and, / with space, collapse whitespace.
        /// </summary>
        private static string NormalizeForMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return System.Text.RegularExpressions.Regex.Replace(
                text.ToLowerInvariant()
                    .Replace("&", " and ")
                    .Replace("/", " ")
                    .Replace("-", " "),
                @"\s+", " ").Trim();
        }

        private List<MustHaveChecklistItem> BuildMustHaveChecklist(EstimateContext context)
        {
            var checklist = new List<MustHaveChecklistItem>();
            if (_mustHaveCategories == null) return checklist;

            // Combine ALL known operations from all sources, normalized for matching
            var allTexts = context.AllOperations
                .Select(o => NormalizeForMatch(o.Part ?? ""))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();

            foreach (var category in _mustHaveCategories)
            {
                foreach (var op in category.Operations)
                {
                    var opNorm = NormalizeForMatch(op.Description);
                    var opWords = opNorm.Split(' ').Where(w => w.Length > 3).Distinct().ToArray();

                    var isPresent = allTexts.Any(d =>
                    {
                        // Exact match
                        if (d == opNorm) return true;
                        // Detected contains full must-have
                        if (d.Contains(opNorm)) return true;
                        // Must-have contains detected, only if detected is >= 45% of must-have length
                        if (opNorm.Contains(d) && d.Length >= opNorm.Length * 0.45)
                            return true;

                        // Proportional word overlap: 60% of must-have words for 3+ word descriptions,
                        // ALL words for short (1-2 word) descriptions
                        if (opWords.Length > 0)
                        {
                            var matchCount = opWords.Count(w => d.Contains(w));
                            int threshold = opWords.Length <= 2
                                ? opWords.Length
                                : (int)Math.Ceiling(opWords.Length * 0.6);
                            if (matchCount >= threshold)
                                return true;
                        }

                        return false;
                    });

                    checklist.Add(new MustHaveChecklistItem
                    {
                        Description = op.Description,
                        Category = category.Name,
                        OperationType = op.OperationType,
                        LaborHours = op.LaborHours,
                        Price = op.Price,
                        MaterialsCost = op.MaterialsCost,
                        WhyNeeded = op.WhyNeeded,
                        Conditions = op.Conditions,
                        IsPresent = isPresent
                    });
                }
            }

            return checklist;
        }

        private void LoadContextMustHaveOperations()
        {
            try
            {
                var dataPath = Path.Combine(AppContext.BaseDirectory, "Data", "MustHaveOperations.json");
                if (!File.Exists(dataPath)) return;

                var json = File.ReadAllText(dataPath);
                var data = JsonSerializer.Deserialize<MustHaveData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                _mustHaveCategories = data?.Categories;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimateContext] Error loading must-haves: {ex.Message}");
            }
        }

        #endregion
    }

    #region Context Models

    public class EstimateContext
    {
        public string? VehicleInfo { get; set; }
        public string? InsuranceCompany { get; set; }
        public List<ContextOperation> EnteredOperations { get; set; } = new();
        public List<ContextOperation> ScreenMonitorOperations { get; set; } = new();
        public List<ContextOperation> ClipboardOperations { get; set; } = new();
        public string? ClipboardSource { get; set; }
        public int TotalEstimatesAnalyzed { get; set; }
        public int TotalPartsLearned { get; set; }
        public int TotalPatternsLearned { get; set; }
        public List<MustHaveChecklistItem> MustHaveChecklist { get; set; } = new();

        /// <summary>
        /// All operations from all sources combined (screen monitor + entered + clipboard)
        /// </summary>
        public List<ContextOperation> AllOperations
        {
            get
            {
                var all = new List<ContextOperation>();
                all.AddRange(ScreenMonitorOperations);
                all.AddRange(EnteredOperations);
                all.AddRange(ClipboardOperations);
                return all;
            }
        }
    }

    public class ContextOperation
    {
        public string Part { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal Hours { get; set; }
        public decimal Price { get; set; }
    }

    public class MustHaveChecklistItem
    {
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal Price { get; set; }
        public decimal MaterialsCost { get; set; }
        public string WhyNeeded { get; set; } = "";
        public string Conditions { get; set; } = "always";
        public bool IsPresent { get; set; }
    }

    // JSON deserialization models
    public class MustHaveData
    {
        public List<MustHaveCategory> Categories { get; set; } = new();
    }

    public class MustHaveCategory
    {
        public string Name { get; set; } = "";
        public List<ContextMustHaveOperation> Operations { get; set; } = new();
    }

    public class ContextMustHaveOperation
    {
        public string Description { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal LaborHours { get; set; }
        public decimal Price { get; set; }
        public decimal MaterialsCost { get; set; }
        public string WhyNeeded { get; set; } = "";
        public string Conditions { get; set; } = "always";
    }

    #endregion
}
