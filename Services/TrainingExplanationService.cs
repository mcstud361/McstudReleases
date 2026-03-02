#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Training Explanation Service - Provides educational context for estimating decisions
    ///
    /// When user clicks "Why?" or hovers over an operation, this service provides:
    /// - Why this operation was added/suggested
    /// - DEG inquiry references
    /// - OEM position statement citations
    /// - "NOT INCLUDED" explanations
    /// - Best practices for new estimators
    /// </summary>
    public class TrainingExplanationService
    {
        private readonly string _notIncludedPath;
        private readonly string _degInquiriesPath;
        private readonly string _missedItemsPath;

        private NotIncludedData? _notIncludedData;
        private DEGInquiriesData? _degData;
        private CommonlyMissedItemsData? _missedItemsData;

        private static TrainingExplanationService? _instance;
        public static TrainingExplanationService Instance => _instance ??= new TrainingExplanationService();

        public TrainingExplanationService()
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
            _notIncludedPath = Path.Combine(dataPath, "NotIncludedOperations.json");
            _degInquiriesPath = Path.Combine(dataPath, "DEGInquiries.json");
            _missedItemsPath = Path.Combine(dataPath, "CommonlyMissedItems.json");

            LoadData();
        }

        private void LoadData()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            try
            {
                if (File.Exists(_notIncludedPath))
                {
                    var json = File.ReadAllText(_notIncludedPath);
                    _notIncludedData = JsonSerializer.Deserialize<NotIncludedData>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Training] NotIncluded load error: {ex.Message}");
            }

            try
            {
                if (File.Exists(_degInquiriesPath))
                {
                    var json = File.ReadAllText(_degInquiriesPath);
                    _degData = JsonSerializer.Deserialize<DEGInquiriesData>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Training] DEG load error: {ex.Message}");
            }

            try
            {
                if (File.Exists(_missedItemsPath))
                {
                    var json = File.ReadAllText(_missedItemsPath);
                    _missedItemsData = JsonSerializer.Deserialize<CommonlyMissedItemsData>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Training] MissedItems load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get comprehensive explanation for an operation
        /// </summary>
        public OperationExplanation GetExplanation(string operationType, string partName, string? context = null)
        {
            var explanation = new OperationExplanation
            {
                OperationType = operationType,
                PartName = partName
            };

            // Build explanation components
            explanation.WhyNeeded = GetWhyNeeded(operationType, partName);
            explanation.NotIncludedInfo = GetNotIncludedInfo(operationType, partName);
            explanation.DegReferences = GetRelevantDEGInquiries(operationType, partName);
            explanation.RelatedOperations = GetRelatedOperations(operationType, partName);
            explanation.BestPractices = GetBestPractices(operationType, partName);
            explanation.CommonMistakes = GetCommonMistakes(operationType, partName);

            return explanation;
        }

        /// <summary>
        /// Explain why a specific supplement item should be added
        /// </summary>
        public string GetSupplementExplanation(string itemName, string triggerOperation)
        {
            var parts = new List<string>();

            // Find in missed items data
            if (_missedItemsData?.OperationChecks != null)
            {
                foreach (var check in _missedItemsData.OperationChecks.Values)
                {
                    var item = check.MissedItems.FirstOrDefault(m =>
                        m.Item.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        parts.Add($"WHY: {item.WhyNeeded}");

                        if (!string.IsNullOrEmpty(item.DegReference))
                        {
                            parts.Add($"REFERENCE: {item.DegReference}");
                        }

                        parts.Add($"TRIGGER: {triggerOperation}");
                        break;
                    }
                }
            }

            // Find related DEG inquiry
            var degInfo = GetDEGByKeyword(itemName);
            if (degInfo != null)
            {
                parts.Add($"DEG #{degInfo.InquiryNumber}: {degInfo.Title}");
                parts.Add($"RESPONSE: {degInfo.Response}");
            }

            return parts.Any() ? string.Join("\n\n", parts) : $"This item is commonly needed when performing {triggerOperation}";
        }

        /// <summary>
        /// Get explanation for why an operation was auto-added
        /// </summary>
        public string GetAutoAddExplanation(string itemName, string reason, string? dependsOn = null)
        {
            var explanation = new List<string>();

            explanation.Add($"AUTO-ADDED: {itemName}");
            explanation.Add($"REASON: {reason}");

            if (!string.IsNullOrEmpty(dependsOn))
            {
                explanation.Add($"BECAUSE OF: {dependsOn}");
            }

            // Add educational context
            var context = GetEducationalContext(itemName);
            if (!string.IsNullOrEmpty(context))
            {
                explanation.Add($"LEARN MORE: {context}");
            }

            return string.Join("\n", explanation);
        }

        /// <summary>
        /// Get "NOT INCLUDED" information for an operation
        /// </summary>
        public List<NotIncludedItem> GetNotIncludedInfo(string operationType, string partName)
        {
            var items = new List<NotIncludedItem>();

            if (_notIncludedData?.Categories == null) return items;

            var searchTerms = new[] { operationType.ToLower(), partName.ToLower() };

            foreach (var category in _notIncludedData.Categories.Values)
            {
                foreach (var item in category.Items)
                {
                    // Check if this NOT INCLUDED item is relevant to the operation
                    var itemLower = item.Item.ToLower();
                    var descLower = item.Description.ToLower();

                    bool isRelevant = false;

                    // Match by operation type
                    if (operationType.ToLower().Contains("refinish") || operationType.ToLower().Contains("paint"))
                    {
                        isRelevant = category.Name.Contains("Refinish") ||
                                    itemLower.Contains("flex") || itemLower.Contains("adhesion") ||
                                    itemLower.Contains("denib") || itemLower.Contains("color");
                    }
                    else if (operationType.ToLower().Contains("replace") && IsStructuralPart(partName))
                    {
                        isRelevant = category.Name.Contains("Structural") ||
                                    itemLower.Contains("weld") || itemLower.Contains("corrosion") ||
                                    itemLower.Contains("seam");
                    }
                    else if (partName.ToLower().Contains("windshield") || partName.ToLower().Contains("glass"))
                    {
                        isRelevant = category.Name.Contains("Glass") || category.Name.Contains("ADAS") ||
                                    itemLower.Contains("camera") || itemLower.Contains("calibration");
                    }

                    if (isRelevant)
                    {
                        items.Add(new NotIncludedItem
                        {
                            Item = item.Item,
                            Description = item.Description,
                            WhyNotIncluded = item.WhyNotIncluded,
                            DegReference = item.DegReference,
                            TypicalTime = item.TypicalTime
                        });
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// Get relevant DEG inquiries for an operation
        /// </summary>
        public List<DEGInquiryReference> GetRelevantDEGInquiries(string operationType, string partName)
        {
            var references = new List<DEGInquiryReference>();

            if (_degData?.Inquiries == null) return references;

            var searchText = $"{operationType} {partName}".ToLower();

            foreach (var inquiry in _degData.Inquiries)
            {
                bool isRelevant = false;

                // Check title and key points
                var titleLower = inquiry.Title.ToLower();
                var responseLower = inquiry.Response.ToLower();

                // Match by common scenarios
                if (searchText.Contains("scan") && (titleLower.Contains("scan") || responseLower.Contains("scan")))
                    isRelevant = true;
                if (searchText.Contains("seam") && titleLower.Contains("seam"))
                    isRelevant = true;
                if (searchText.Contains("quarter") && titleLower.Contains("quarter"))
                    isRelevant = true;
                if (searchText.Contains("roof") && titleLower.Contains("roof"))
                    isRelevant = true;
                if (searchText.Contains("bumper") && (titleLower.Contains("bumper") || titleLower.Contains("radar")))
                    isRelevant = true;
                if (searchText.Contains("rivet") && titleLower.Contains("rivet"))
                    isRelevant = true;
                if (searchText.Contains("frame") && titleLower.Contains("frame"))
                    isRelevant = true;

                if (isRelevant)
                {
                    references.Add(new DEGInquiryReference
                    {
                        InquiryNumber = inquiry.InquiryNumber,
                        Title = inquiry.Title,
                        Summary = inquiry.Response,
                        KeyPoints = inquiry.KeyPoints.ToList(),
                        Systems = inquiry.Systems.ToList(),
                        VerifyUrl = "https://degweb.org/deg-database/"
                    });
                }
            }

            return references;
        }

        /// <summary>
        /// Get DEG inquiry by keyword search
        /// </summary>
        public DEGInquiry? GetDEGByKeyword(string keyword)
        {
            if (_degData?.Inquiries == null) return null;

            var keywordLower = keyword.ToLower();

            return _degData.Inquiries.FirstOrDefault(i =>
                i.Title.ToLower().Contains(keywordLower) ||
                i.Response.ToLower().Contains(keywordLower) ||
                i.KeyPoints.Any(kp => kp.ToLower().Contains(keywordLower)));
        }

        /// <summary>
        /// Get DEG inquiry by number
        /// </summary>
        public DEGInquiry? GetDEGByNumber(string inquiryNumber)
        {
            if (_degData?.Inquiries == null) return null;

            return _degData.Inquiries.FirstOrDefault(i =>
                i.InquiryNumber.Contains(inquiryNumber));
        }

        /// <summary>
        /// Get best practices for an operation
        /// </summary>
        private List<string> GetBestPractices(string operationType, string partName)
        {
            var practices = new List<string>();

            var opLower = operationType.ToLower();
            var partLower = partName.ToLower();

            // Structural work best practices
            if (IsStructuralPart(partName) && opLower.Contains("replace"))
            {
                practices.Add("Always apply weld-thru primer before welding");
                practices.Add("Document measurements before and after repair");
                practices.Add("Apply corrosion protection to all weld seams and cavities");
                practices.Add("Match OEM seam sealer appearance");
            }

            // Refinish best practices
            if (opLower.Contains("refinish") || opLower.Contains("paint"))
            {
                practices.Add("Always use flex additive on plastic parts");
                practices.Add("Apply adhesion promoter to bare plastic");
                practices.Add("Check for 3-stage/tri-coat paint (add 50% refinish time)");
                practices.Add("Consider blend on adjacent panels for best color match");
            }

            // Bumper best practices
            if (partLower.Contains("bumper"))
            {
                practices.Add("Check for radar sensors behind bumper cover");
                practices.Add("No metallic paint in radar detection zone");
                practices.Add("Account for parking sensor transfer/calibration");
            }

            // Glass best practices
            if (partLower.Contains("windshield") || partLower.Contains("glass"))
            {
                practices.Add("Always recalibrate forward camera after windshield work");
                practices.Add("Observe urethane cure time before releasing vehicle");
                practices.Add("Transfer rain sensor carefully to avoid damage");
            }

            return practices;
        }

        /// <summary>
        /// Get common mistakes to avoid
        /// </summary>
        private List<string> GetCommonMistakes(string operationType, string partName)
        {
            var mistakes = new List<string>();

            var opLower = operationType.ToLower();
            var partLower = partName.ToLower();

            // Common structural mistakes
            if (IsStructuralPart(partName))
            {
                mistakes.Add("Forgetting corrosion protection - leads to rust warranty claims");
                mistakes.Add("Not documenting measurements - liability exposure");
                mistakes.Add("Skipping weld-thru primer - OEM requirement");
            }

            // Common refinish mistakes
            if (opLower.Contains("refinish"))
            {
                mistakes.Add("No flex additive on plastic - paint will crack");
                mistakes.Add("Not checking paint code for 3-stage");
                mistakes.Add("Missing adjacent blend - color won't match");
            }

            // Common ADAS mistakes
            if (partLower.Contains("bumper") || partLower.Contains("windshield"))
            {
                mistakes.Add("Forgetting ADAS calibration - safety system won't work properly");
                mistakes.Add("Not masking radar zone during refinish");
            }

            // Common diagnostic mistakes
            mistakes.Add("Skipping pre-scan - can't prove pre-existing conditions");
            mistakes.Add("Skipping post-scan - systems may have codes");

            return mistakes;
        }

        /// <summary>
        /// Get why an operation is needed
        /// </summary>
        private string GetWhyNeeded(string operationType, string partName)
        {
            var opLower = operationType.ToLower();
            var partLower = partName.ToLower();

            // Common explanations
            if (opLower.Contains("blend"))
                return "Blending adjacent panels helps achieve invisible color match since new paint will appear different from aged paint.";

            if (opLower.Contains("corrosion"))
                return "Corrosion protection is required by OEMs in all weld areas and enclosed cavities to prevent rust that would compromise structural integrity.";

            if (opLower.Contains("weld") && opLower.Contains("primer"))
                return "Weld-thru primer provides corrosion protection at weld joints where bare metal is exposed during welding.";

            if (opLower.Contains("seam sealer"))
                return "Seam sealer prevents water intrusion at panel joints and replicates factory appearance for proper repair.";

            if (opLower.Contains("calibration") || opLower.Contains("adas"))
                return "ADAS sensors must be properly aimed to function correctly. Improper calibration can result in safety system failure.";

            if (opLower.Contains("scan"))
                return "Diagnostic scans document system status before repair (proving pre-existing issues) and after repair (verifying all systems operational).";

            if (opLower.Contains("flex"))
                return "Flex additive allows paint to flex with plastic substrates without cracking.";

            if (opLower.Contains("adhesion"))
                return "Adhesion promoter chemically bonds to plastic surfaces, allowing paint to properly adhere.";

            return $"{operationType} is required for proper repair of {partName}";
        }

        /// <summary>
        /// Get operations that typically accompany this one
        /// </summary>
        private List<string> GetRelatedOperations(string operationType, string partName)
        {
            var related = new List<string>();

            var opLower = operationType.ToLower();
            var partLower = partName.ToLower();

            // Replace → Refinish
            if (opLower.Contains("replace") && !partLower.Contains("glass") && !partLower.Contains("light"))
            {
                related.Add("Refinish (new panel needs paint)");
                related.Add("Adjacent panel blend");
            }

            // Structural replace → Full suite
            if (IsStructuralPart(partName) && opLower.Contains("replace"))
            {
                related.Add("Weld-thru primer");
                related.Add("Corrosion protection");
                related.Add("Seam sealer");
                related.Add("R&I interior trim for access");
                related.Add("Pre/post measurements");
            }

            // Bumper → ADAS
            if (partLower.Contains("bumper"))
            {
                related.Add("Parking sensor transfer/calibration");
                related.Add("Radar calibration (if equipped)");
            }

            // Windshield → Camera
            if (partLower.Contains("windshield"))
            {
                related.Add("Forward camera calibration");
                related.Add("Rain sensor transfer");
            }

            return related;
        }

        /// <summary>
        /// Get educational context for learning
        /// </summary>
        private string GetEducationalContext(string itemName)
        {
            var itemLower = itemName.ToLower();

            if (itemLower.Contains("deg"))
                return "DEG (Database Enhancement Gateway) is where shops submit questions about estimating database accuracy. Responses clarify what IS and IS NOT INCLUDED in labor times.";

            if (itemLower.Contains("not included"))
                return "NOT INCLUDED operations are legitimate repair tasks that the estimating databases do not account for in their published labor times.";

            if (itemLower.Contains("p-page"))
                return "P-Pages are the procedure pages in CCC, Mitchell, and Audatex that define what operations ARE and ARE NOT INCLUDED in labor times.";

            if (itemLower.Contains("oem") || itemLower.Contains("position"))
                return "OEM Position Statements are official manufacturer guidance on how vehicles should be repaired to maintain warranty and safety compliance.";

            return "";
        }

        /// <summary>
        /// Check if part is structural (welded)
        /// </summary>
        private bool IsStructuralPart(string partName)
        {
            var structural = new[] { "quarter", "roof", "rocker", "pillar", "rail", "floor", "apron", "strut tower" };
            return structural.Any(s => partName.ToLower().Contains(s));
        }

        /// <summary>
        /// Get quick tips for display in UI
        /// </summary>
        public List<string> GetQuickTips()
        {
            return _notIncludedData?.QuickReference?.AlwaysConsider?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Get commonly forgotten items for display
        /// </summary>
        public List<string> GetCommonlyForgotten()
        {
            return _notIncludedData?.QuickReference?.CommonlyForgotten?.ToList() ?? new List<string>();
        }
    }

    #region Training Data Models

    public class NotIncludedData
    {
        public string Version { get; set; } = "";
        public string Source { get; set; } = "";
        public Dictionary<string, NotIncludedCategory> Categories { get; set; } = new();
        public QuickReferenceData? QuickReference { get; set; }
    }

    public class NotIncludedCategory
    {
        public string Name { get; set; } = "";
        public List<NotIncludedItemData> Items { get; set; } = new();
    }

    public class NotIncludedItemData
    {
        public string Item { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNotIncluded { get; set; } = "";
        public string? DegReference { get; set; }
        public object? TypicalTime { get; set; }
        public string? Notes { get; set; }
    }

    public class QuickReferenceData
    {
        public string[] AlwaysConsider { get; set; } = Array.Empty<string>();
        public string[] CommonlyForgotten { get; set; } = Array.Empty<string>();
    }

    public class DEGInquiriesData
    {
        public string Version { get; set; } = "";
        public List<DEGInquiry> Inquiries { get; set; } = new();
    }

    public class DEGInquiry
    {
        public string Id { get; set; } = "";
        public string InquiryNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Vehicle { get; set; } = "";
        public string Response { get; set; } = "";
        public string[] KeyPoints { get; set; } = Array.Empty<string>();
        public string[] Systems { get; set; } = Array.Empty<string>();
        public string? CitedIn { get; set; }
    }

    // Output models
    public class OperationExplanation
    {
        public string OperationType { get; set; } = "";
        public string PartName { get; set; } = "";
        public string WhyNeeded { get; set; } = "";
        public List<NotIncludedItem> NotIncludedInfo { get; set; } = new();
        public List<DEGInquiryReference> DegReferences { get; set; } = new();
        public List<string> RelatedOperations { get; set; } = new();
        public List<string> BestPractices { get; set; } = new();
        public List<string> CommonMistakes { get; set; } = new();
    }

    public class NotIncludedItem
    {
        public string Item { get; set; } = "";
        public string Description { get; set; } = "";
        public string WhyNotIncluded { get; set; } = "";
        public string? DegReference { get; set; }
        public object? TypicalTime { get; set; }
    }

    public class DEGInquiryReference
    {
        public string InquiryNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<string> KeyPoints { get; set; } = new();
        public List<string> Systems { get; set; } = new();
        public string VerifyUrl { get; set; } = "";
    }

    #endregion
}
