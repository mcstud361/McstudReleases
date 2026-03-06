#nullable enable
using System.Collections.Generic;

namespace McStudDesktop.Services
{
    public record VersionEntry(string Version, string Date, string[] Changes);

    public static class ChangelogData
    {
        public static List<VersionEntry> GetChangelog()
        {
            return new List<VersionEntry>
            {
                new VersionEntry("v2.2.0", "March 2026", new[]
                {
                    "Welcome walkthrough for new users on first launch",
                    "Spotlight tour highlights each tab with descriptions",
                    "What's New dialog when app updates to a new version",
                    "Replay Tour button in Settings tab",
                    "Login auth, text-to-speech, and price catalog features",
                    "Learned patterns and template form improvements"
                }),
                new VersionEntry("v1.0.5", "March 2026", new[]
                {
                    "Auto-matched references now go to a staging panel for review before adding to PDF queue",
                    "Checkbox list grouped by source type (Definitions, P-Pages, DEG, Procedures, Incl/Not Incl)",
                    "Add Selected, Add All, and Clear actions for staged reference matches",
                    "OCR and estimate upload matches merge into staging without duplicates"
                }),
                new VersionEntry("v1.0.4", "February 2026", new[]
                {
                    "Consolidated Shop Docs tab - Invoices and PPF now under Shop Docs",
                    "Custom checklists - duplicate, edit, create your own checklists",
                    "Checklist editor with add/remove sections and items",
                    "Material suggestions - consumables needed per repair operation",
                    "PDF export for Color Tint, Shop Stock, and PPF invoices",
                    "Consistent 'Export to PDF' wording across all documents",
                    "Driveable vehicle assessment checklist"
                }),
                new VersionEntry("v1.0.3", "February 2026", new[]
                {
                    "Added What's New section in Settings",
                    "Removed redundant help button from tab bar"
                }),
                new VersionEntry("v1.0.0", "February 2026", new[]
                {
                    "Export to CCC Desktop and Mitchell",
                    "Smart Paste with auto-detection",
                    "Clipboard monitoring",
                    "Chat assistant",
                    "PDF estimate import",
                    "Export statistics",
                    "Reference library (Definitions, DEG, P-Pages, Procedures)",
                    "Auto-update"
                })
            };
        }
    }
}
