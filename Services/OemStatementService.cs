#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Loads OEM position statements and matches them to a vehicle's manufacturer, so the
    /// estimate scrubber can surface "this is a Subaru — here are Subaru's scanning/calibration
    /// position statements" without the user hunting through the Reference tab.
    ///
    /// This is also the single source of truth for which source links sit behind a manufacturer
    /// login/subscription wall (see <see cref="RequiresLogin"/>) — the Reference views defer to it.
    /// </summary>
    public class OemStatementService
    {
        private static readonly Lazy<OemStatementService> _instance = new(() => new OemStatementService());
        public static OemStatementService Instance => _instance.Value;

        private List<OemStatementRaw> _statements = new();
        private bool _loaded;
        private readonly object _lock = new();

        // Manufacturer tech portals / aggregators that require a paid subscription or certified
        // login. We never present these as clickable links because not every user has access.
        private static readonly string[] LoginGatedHosts =
        {
            "techinfo.honda.com", "techinfo.toyota.com", "acdelcotds.com", "motorcraftservice.com",
            "techauthority.com", "nissan-techinfo.com", "techinfo.subaru.com", "hyundaitechinfo.com",
            "kiatechinfo.com", "erwin.vw.com", "bmwtis.com", "startekinfo.com", "mazdaserviceinfo.com",
            "jlrext.com", "mitsubishitechinfo.com", "service.tesla.com", "business.rivian.com",
            "mitchell1.com", "alldata.com"
        };

        /// <summary>True if the URL sits behind a manufacturer login/subscription wall.</summary>
        public static bool RequiresLogin(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host;
            foreach (var gated in LoginGatedHosts)
            {
                if (host.Equals(gated, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + gated, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // A vehicle make (as it appears in an estimate) -> the substring that identifies its
        // statement group in the JSON's "oem" field. GM brands are the tricky case: the estimate
        // says "Chevrolet" but the statement group is labelled "General Motors".
        private static readonly Dictionary<string, string> MakeToOemNeedle = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chevrolet"] = "general motors", ["chevy"] = "general motors", ["gmc"] = "general motors",
            ["buick"] = "general motors", ["cadillac"] = "general motors",
            ["honda"] = "honda", ["acura"] = "acura",
            ["toyota"] = "toyota", ["lexus"] = "lexus", ["scion"] = "toyota",
            ["ford"] = "ford", ["lincoln"] = "lincoln", ["mercury"] = "ford",
            ["chrysler"] = "chrysler", ["dodge"] = "dodge", ["jeep"] = "jeep", ["ram"] = "ram", ["fiat"] = "stellantis",
            ["nissan"] = "nissan", ["infiniti"] = "infiniti",
            ["subaru"] = "subaru",
            ["hyundai"] = "hyundai", ["kia"] = "kia", ["genesis"] = "genesis",
            ["volkswagen"] = "volkswagen", ["vw"] = "volkswagen", ["audi"] = "audi",
            ["bmw"] = "bmw", ["mini"] = "mini",
            ["mercedes-benz"] = "mercedes", ["mercedes"] = "mercedes", ["benz"] = "mercedes",
            ["mazda"] = "mazda",
            ["volvo"] = "volvo",
            ["land rover"] = "land rover", ["range rover"] = "land rover", ["jaguar"] = "jaguar",
            ["mitsubishi"] = "mitsubishi",
            ["porsche"] = "porsche",
            ["tesla"] = "tesla",
            ["rivian"] = "rivian",
            ["lucid"] = "lucid",
            ["polestar"] = "polestar"
        };

        private void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "Data", "OEMPositionStatements.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var data = JsonSerializer.Deserialize<OemStatementsFile>(json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (data?.Statements != null)
                            _statements = data.Statements;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OemStatementService] Load error: {ex.Message}");
                }
                _loaded = true;
            }
        }

        /// <summary>
        /// Extract the manufacturer from a raw vehicle string like "2023 Subaru Outback".
        /// Returns the recognized make (proper-cased) or null if no known make is present.
        /// </summary>
        public string? ExtractMake(string? vehicleInfo)
        {
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return null;
            var text = vehicleInfo.ToLowerInvariant();
            // Longest keys first so "land rover" wins over a bare "rover"-style partial.
            foreach (var make in MakeToOemNeedle.Keys.OrderByDescending(k => k.Length))
            {
                var pattern = @"\b" + Regex.Escape(make) + @"\b";
                if (Regex.IsMatch(text, pattern))
                    return CultureInfoTitleCase(make);
            }
            return null;
        }

        /// <summary>
        /// Return the OEM position statements that apply to the estimate's vehicle manufacturer.
        /// Empty list if the make is unknown or no statements match.
        /// </summary>
        public List<OemStatementMatch> GetForVehicle(string? vehicleInfo)
        {
            EnsureLoaded();
            var result = new List<OemStatementMatch>();
            if (string.IsNullOrWhiteSpace(vehicleInfo)) return result;

            var text = vehicleInfo.ToLowerInvariant();
            string? needle = null;
            foreach (var make in MakeToOemNeedle.Keys.OrderByDescending(k => k.Length))
            {
                if (Regex.IsMatch(text, @"\b" + Regex.Escape(make) + @"\b"))
                {
                    needle = MakeToOemNeedle[make];
                    break;
                }
            }
            if (needle == null) return result;

            foreach (var s in _statements)
            {
                var oem = s.Oem?.ToLowerInvariant() ?? "";
                if (!oem.Contains(needle)) continue;

                result.Add(new OemStatementMatch
                {
                    Id = s.Id,
                    Oem = s.Oem ?? "",
                    Title = s.Title ?? "",
                    Category = s.Category ?? "",
                    Summary = s.Summary ?? "",
                    KeyPoints = s.KeyPoints ?? new List<string>(),
                    PublicLink = BestPublicLink(s)
                });
            }

            // Scanning first, then ADAS calibration, then the rest — most-relevant on top.
            return result
                .OrderBy(m => m.Category == "Scanning" ? 0 : m.Category == "ADAS Calibration" ? 1 : 2)
                .ToList();
        }

        /// <summary>Pick the first source link that is NOT behind a login wall, or null if none.</summary>
        public static string? BestPublicLink(OemStatementRaw s)
        {
            foreach (var url in new[] { s.PdfLink, s.IcarLink, s.Link, s.FreeResources, s.CalibrationLink,
                                        s.AdasLink, s.AdasJobAid, s.CollisionLink })
            {
                if (!string.IsNullOrEmpty(url) && !RequiresLogin(url))
                    return url;
            }
            return null;
        }

        private static string CultureInfoTitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Special-case the all-caps abbreviations.
            if (s is "vw" or "bmw" or "gmc") return s.ToUpperInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
        }

        // --- JSON shapes -------------------------------------------------------

        private class OemStatementsFile
        {
            public List<OemStatementRaw>? Statements { get; set; }
        }

        public class OemStatementRaw
        {
            public string? Id { get; set; }
            public string? Oem { get; set; }
            public string? Title { get; set; }
            public string? Category { get; set; }
            public string? Summary { get; set; }
            public List<string>? KeyPoints { get; set; }
            public string? Link { get; set; }
            public string? PdfLink { get; set; }
            public string? IcarLink { get; set; }
            public string? CollisionLink { get; set; }
            public string? AdasLink { get; set; }
            public string? CalibrationLink { get; set; }
            public string? AdasJobAid { get; set; }
            public string? FreeResources { get; set; }
        }
    }

    /// <summary>A manufacturer position statement matched to the estimate's vehicle.</summary>
    public class OemStatementMatch
    {
        public string? Id { get; set; }
        public string Oem { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<string> KeyPoints { get; set; } = new();
        /// <summary>A non-login source link for verification, or null if none is public.</summary>
        public string? PublicLink { get; set; }
    }
}
