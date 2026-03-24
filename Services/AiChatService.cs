#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace McStudDesktop.Services
{
    /// <summary>
    /// AI-powered chat service that builds rich system prompts with full estimate context.
    /// Supports multi-turn conversation, estimate review, and smart defaults.
    /// Falls back gracefully — all methods return null on failure.
    /// </summary>
    public class AiChatService
    {
        private static AiChatService? _instance;
        public static AiChatService Instance => _instance ??= new AiChatService();

        private readonly List<ChatTurn> _conversationHistory = new();
        private const int MAX_HISTORY_TURNS = 10;

        /// <summary>
        /// Send a user message with full context. Returns null if AI is unavailable.
        /// Maintains conversation history for multi-turn context.
        /// </summary>
        public async Task<AiChatResponse?> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var systemPrompt = BuildSystemPrompt();
                var messages = BuildMessages(userMessage);

                var response = await ClaudeApiService.Instance.SendWithHistoryAsync(
                    systemPrompt, messages, AiFeature.Chat, 2048, cancellationToken);

                if (response == null) return null;

                // Record in conversation history
                _conversationHistory.Add(new ChatTurn { Role = "user", Content = userMessage });
                _conversationHistory.Add(new ChatTurn { Role = "assistant", Content = response.Text });

                // Trim history
                while (_conversationHistory.Count > MAX_HISTORY_TURNS * 2)
                    _conversationHistory.RemoveAt(0);

                return new AiChatResponse
                {
                    Message = response.Text,
                    InputTokens = response.InputTokens,
                    OutputTokens = response.OutputTokens,
                    LatencyMs = response.LatencyMs,
                    FollowUps = ExtractFollowUps(response.Text)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiChat] ChatAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Dedicated estimate review that checks must-haves, commonly missed items,
        /// ADAS requirements, and blend requirements. Returns null if AI unavailable.
        /// </summary>
        public async Task<AiChatResponse?> ReviewEstimateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var contextService = EstimateContextService.Instance;
                var contextText = contextService.BuildContextText();

                if (string.IsNullOrWhiteSpace(contextText) || contextText.Length < 20)
                {
                    return new AiChatResponse
                    {
                        Message = "No estimate data available to review. Enter some operations in the chat or load an estimate first.",
                        FollowUps = new List<string> { "What are the must-have operations?", "Help me build an estimate" }
                    };
                }

                var systemPrompt = BuildReviewSystemPrompt();
                var userMessage = $"Please review my current estimate and identify what's missing:\n\n{contextText}";

                var response = await ClaudeApiService.Instance.SendAsync(
                    systemPrompt, userMessage, AiFeature.EstimateReview, 4096, cancellationToken);

                if (response == null) return null;

                return new AiChatResponse
                {
                    Message = response.Text,
                    InputTokens = response.InputTokens,
                    OutputTokens = response.OutputTokens,
                    LatencyMs = response.LatencyMs,
                    FollowUps = new List<string>
                    {
                        "What am I missing?",
                        "Explain the must-have operations",
                        "What ADAS calibrations are needed?"
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiChat] ReviewEstimateAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// AI suggests which operations and inputs to set based on damage and vehicle.
        /// Returns null if AI unavailable.
        /// </summary>
        public async Task<AiChatResponse?> SuggestDefaultsAsync(string damageDescription, string? vehicleInfo = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var systemPrompt = BuildDefaultsSystemPrompt();

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(vehicleInfo))
                    sb.AppendLine($"Vehicle: {vehicleInfo}");
                sb.AppendLine($"Damage: {damageDescription}");

                // Include must-haves for reference
                var mustHaves = EstimateContextService.Instance.GetAllContextMustHaveOperations();
                if (mustHaves.Count > 0)
                {
                    sb.AppendLine("\nAvailable must-have operations:");
                    foreach (var op in mustHaves)
                    {
                        var detail = op.LaborHours > 0 ? $"{op.LaborHours:N1}h" : $"${op.Price:N2}";
                        sb.AppendLine($"  - {op.Description} ({op.OperationType}, {detail})");
                    }
                }

                var response = await ClaudeApiService.Instance.SendAsync(
                    systemPrompt, sb.ToString(), AiFeature.Chat, 2048, cancellationToken);

                if (response == null) return null;

                return new AiChatResponse
                {
                    Message = response.Text,
                    InputTokens = response.InputTokens,
                    OutputTokens = response.OutputTokens,
                    LatencyMs = response.LatencyMs,
                    FollowUps = new List<string>
                    {
                        "Review my estimate",
                        "What ADAS calibrations are needed?",
                        "What else should I add?"
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiChat] SuggestDefaultsAsync error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear conversation history (e.g., when starting a new estimate)
        /// </summary>
        public void ClearHistory()
        {
            _conversationHistory.Clear();
        }

        /// <summary>
        /// Whether the AI chat feature is currently available
        /// </summary>
        public bool IsAvailable => AiConfigService.Instance.IsFeatureEnabled(AiFeature.Chat);

        /// <summary>
        /// Whether the AI estimate review feature is currently available
        /// </summary>
        public bool IsReviewAvailable => AiConfigService.Instance.IsFeatureEnabled(AiFeature.EstimateReview);

        #region System Prompt Builders

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are McStud AI, an expert collision repair estimating assistant built into the McStud Estimating Tool.");
            sb.AppendLine();
            sb.AppendLine("## Your Knowledge");
            sb.AppendLine("- You help collision repair estimators write thorough, accurate estimates");
            sb.AppendLine("- Tabs: Export, Chat, Guide, Import, Reference, Settings, Shop Docs, Stats");
            sb.AppendLine("- The Excel engine has 290 inputs across 10 sheets: SOP List, Part Ops, Body Ops, Refinish, Mechanical, SRS, Total Loss, Body On Frame, Stolen Recovery, Cover Car");
            sb.AppendLine("- Operation types: Rpr (Repair), Repl/Replace, R&I (Remove & Install), Refn (Refinish), Blnd (Blend), Add (Additional), O/H (Overhaul), Subl (Sublet), Mech (Mechanical), Body");
            sb.AppendLine();

            // Must-have operations
            sb.AppendLine("## Must-Have Operations (should be on EVERY estimate)");
            var mustHaves = EstimateContextService.Instance.GetMustHaveCategories();
            foreach (var category in mustHaves)
            {
                sb.AppendLine($"\n### {category.Name}");
                foreach (var op in category.Operations)
                {
                    var detail = op.LaborHours > 0 ? $"{op.LaborHours:N1}h" : $"${op.Price:N2}";
                    var materials = op.MaterialsCost > 0 ? $", ${op.MaterialsCost:N2} materials" : "";
                    sb.AppendLine($"- {op.Description} ({op.OperationType}, {detail}{materials}) — {op.Conditions}");
                }
            }
            sb.AppendLine();

            // Current context
            var contextText = EstimateContextService.Instance.BuildContextText();
            if (!string.IsNullOrWhiteSpace(contextText))
            {
                sb.AppendLine("## Current Estimate Context");
                sb.AppendLine(contextText);
                sb.AppendLine();
            }

            sb.AppendLine("## Rules");
            sb.AppendLine("- Never reveal insurance companies, claim numbers, VINs, or other sensitive data");
            sb.AppendLine("- Focus on collision repair estimating — this is your domain");
            sb.AppendLine("- When suggesting operations, include the operation type, hours, and price");
            sb.AppendLine("- Flag missing must-have operations proactively");
            sb.AppendLine("- Reference P-Pages and DEG inquiries when relevant");
            sb.AppendLine("- Be concise and practical — estimators are busy");
            sb.AppendLine("- Use industry terminology naturally");
            sb.AppendLine("- If the current estimate has operations, analyze them and suggest what's missing");

            return sb.ToString();
        }

        private string BuildReviewSystemPrompt()
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are McStud AI reviewing a collision repair estimate. Provide a structured, thorough review.");
            sb.AppendLine();
            sb.AppendLine("## Review Checklist");
            sb.AppendLine("1. **Must-Have Operations** — Check if all standard operations are present (battery disconnect, scans, clean for delivery, etc.)");
            sb.AppendLine("2. **Commonly Missed Items** — For each part being worked on, check for related R&I, materials, and additionals");
            sb.AppendLine("3. **ADAS Requirements** — If vehicle is 2015+ or has ADAS components, check for calibration requirements");
            sb.AppendLine("4. **Blend Requirements** — Adjacent panels to any refinished panel should have blend consideration");
            sb.AppendLine("5. **Materials** — Corrosion protection, weld-thru primer, seam sealer for structural panels");
            sb.AppendLine("6. **Diagnostics** — Pre-scan, in-process scan, post-scan for any estimate over $2,500");
            sb.AppendLine();

            // Include must-haves
            sb.AppendLine("## Must-Have Operations Reference");
            var mustHaves = EstimateContextService.Instance.GetMustHaveCategories();
            foreach (var category in mustHaves)
            {
                sb.AppendLine($"\n### {category.Name}");
                foreach (var op in category.Operations)
                {
                    var detail = op.LaborHours > 0 ? $"{op.LaborHours:N1}h" : $"${op.Price:N2}";
                    sb.AppendLine($"- {op.Description} ({op.OperationType}, {detail}) — {op.WhyNeeded}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Output Format");
            sb.AppendLine("Structure your review as:");
            sb.AppendLine("1. **Score** — Rate 0-100 with letter grade");
            sb.AppendLine("2. **Missing Must-Haves** — List each missing must-have with why it's needed");
            sb.AppendLine("3. **Missing Per-Part Items** — For each part, what related operations are missing");
            sb.AppendLine("4. **ADAS/Calibration** — Any calibration needs");
            sb.AppendLine("5. **Recommendations** — Top 3 highest-priority additions");
            sb.AppendLine();
            sb.AppendLine("Be specific: include operation type, hours, and price for every suggestion.");

            return sb.ToString();
        }

        private string BuildDefaultsSystemPrompt()
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are McStud AI. Given a damage description and vehicle info, suggest the operations that should be on the estimate.");
            sb.AppendLine();
            sb.AppendLine("## Output Format");
            sb.AppendLine("List suggested operations grouped by category:");
            sb.AppendLine("1. **Affected Panels** — Main repair/replace operations with estimated hours");
            sb.AppendLine("2. **Related R&I** — Remove and install operations for adjacent components");
            sb.AppendLine("3. **Refinish** — Paint, blend, and material operations");
            sb.AppendLine("4. **Must-Haves** — Standard operations from the must-have list that apply");
            sb.AppendLine("5. **ADAS/Calibrations** — If applicable for this vehicle/damage");
            sb.AppendLine();
            sb.AppendLine("For each operation include: description, operation type (Rpr/Replace/R&I/Refn/Blnd/etc), estimated hours or price.");
            sb.AppendLine("Be practical and thorough — include everything a complete estimate would need.");

            return sb.ToString();
        }

        #endregion

        #region Private Helpers

        private List<ClaudeMessage> BuildMessages(string userMessage)
        {
            var messages = new List<ClaudeMessage>();

            // Add conversation history
            foreach (var turn in _conversationHistory)
            {
                messages.Add(new ClaudeMessage { Role = turn.Role, Content = turn.Content });
            }

            // Add current user message
            messages.Add(new ClaudeMessage { Role = "user", Content = userMessage });

            return messages;
        }

        private static List<string> ExtractFollowUps(string responseText)
        {
            var followUps = new List<string>();

            // If the response mentions missing operations, suggest asking about them
            var lower = responseText.ToLowerInvariant();
            if (lower.Contains("missing") || lower.Contains("should add") || lower.Contains("recommend"))
                followUps.Add("What else am I missing?");
            if (lower.Contains("adas") || lower.Contains("calibrat"))
                followUps.Add("What ADAS calibrations are needed?");
            if (lower.Contains("blend") || lower.Contains("adjacent"))
                followUps.Add("Show blend requirements");
            if (lower.Contains("scan") || lower.Contains("diagnostic"))
                followUps.Add("Explain the scan requirements");

            // Always offer review if not already reviewing
            if (!lower.Contains("score") && followUps.Count < 3)
                followUps.Add("Review my estimate");

            return followUps.Take(4).ToList();
        }

        #endregion
    }

    #region Chat Models

    public class AiChatResponse
    {
        public string Message { get; set; } = "";
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public long LatencyMs { get; set; }
        public List<string> FollowUps { get; set; } = new();
    }

    internal class ChatTurn
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
