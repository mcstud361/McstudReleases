#nullable enable
using System.Collections.Generic;

namespace McStudDesktop.Services
{
    public record TermsClause(string Heading, string Body);

    /// <summary>
    /// The McStud Terms of Use — single source of truth for the first-launch notice
    /// and the Settings "Terms of Use" section.
    ///
    /// Bump <see cref="Version"/> whenever the wording changes; the first-launch notice
    /// re-appears once for every user until they accept the new version.
    /// </summary>
    public static class TermsOfUseData
    {
        /// <summary>Current terms version. Increment when the text below changes.</summary>
        public const string Version = "1.0";

        public static IReadOnlyList<TermsClause> GetClauses() => new List<TermsClause>
        {
            new TermsClause(
                "1. Acceptance",
                "By installing or using McStud, you agree to these Terms. If you do not agree, do not use the software."),

            new TermsClause(
                "2. Independent Tool",
                "McStud is an independent product and is not affiliated with, endorsed by, or connected to CCC, Mitchell, Audatex, any insurance carrier, or any other estimating system. It assists you within those systems but does not control them and is not responsible for their accuracy, availability, or behavior."),

            new TermsClause(
                "3. Your Responsibility",
                "McStud is an educational and informational tool to support your estimating — not a replacement for your professional judgment. You are solely responsible for every estimate you produce. You must verify that each part, operation, and line item matches the work actually performed, and you are responsible for keeping the documentation to prove it."),

            new TermsClause(
                "4. Reference Materials",
                "McStud may include or reference industry materials such as DEG (Database Enhancement Gateway) entries, P-Pages, and OEM position statements. This information comes from publicly available industry sources, may change over time, and is provided for reference only. You are responsible for confirming any procedure or figure against the current official source before relying on it."),

            new TermsClause(
                "5. No Warranty & No Liability",
                "McStud is provided \"as is,\" without warranties of any kind. To the fullest extent allowed by law, we are not liable for any damages arising from your use of the software."),

            new TermsClause(
                "6. Acceptance of These Terms",
                "Continued use of McStud means you accept these Terms and any future updates to them."),
        };
    }
}
