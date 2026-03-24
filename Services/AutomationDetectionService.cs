#nullable enable
using System;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Static helper to detect whether a chat message is an automation command
    /// (e.g., "replace the hood") vs. a question or general chat message.
    /// </summary>
    public static class AutomationDetectionService
    {
        private static readonly string[] TriggerPhrases = new[]
        {
            "replace the ", "repair the ", "refinish the ", "blend the ",
            "r&i the ", "r and i the ", "remove the ",
            "add replace ", "add repair ", "add refinish ", "add blend ",
            "add r&i ", "add r and i ", "add sublet ", "add mech ",
            "add body ", "add o/h ", "add overhaul ",
            "enter replace ", "enter repair ", "enter refinish ",
            "insert replace ", "insert repair ", "insert refinish ",
            "replace hood", "replace fender", "replace bumper", "replace door",
            "repair hood", "repair fender", "repair bumper", "repair door",
        };

        // Patterns that indicate a question, not a command
        private static readonly string[] QuestionPrefixes = new[]
        {
            "what is ", "what's ", "what are ", "how do ", "how does ",
            "should i ", "can i ", "why ", "when ", "where ", "who ",
            "is it ", "is there ", "do i ", "does ", "explain ",
            "tell me about ", "what does ",
        };

        /// <summary>
        /// Returns true if the message looks like an automation command
        /// that should be handled by the AI automation pipeline.
        /// </summary>
        public static bool IsAutomationCommand(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var lower = message.Trim().ToLowerInvariant();

            // Questions are never automation commands
            if (lower.EndsWith("?")) return false;
            foreach (var prefix in QuestionPrefixes)
            {
                if (lower.StartsWith(prefix)) return false;
            }

            // Check for trigger phrases
            foreach (var phrase in TriggerPhrases)
            {
                if (lower.StartsWith(phrase) || lower.Contains(" " + phrase))
                    return true;
            }

            // Pattern: "<operation> <part>" like "replace hood" or "repair fender"
            var opPartPattern = @"^(replace|repair|refinish|blend|r&i|overhaul|o/h|sublet|mech|body)\s+\w";
            if (Regex.IsMatch(lower, opPartPattern))
                return true;

            return false;
        }
    }
}
