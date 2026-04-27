#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

public class DRPChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public string? Notes { get; set; }
    /// <summary>Keyword(s) to look for in the estimate text. Comma-separated.</summary>
    public string? EstimateKeywords { get; set; }
    public bool IsRequired { get; set; } = true;
}

public class InsurerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Notes { get; set; }

    /// <summary>Agreed labor rates per operation type (e.g., "Body": 52.00)</summary>
    public Dictionary<string, decimal> AgreedRates { get; set; } = new();

    /// <summary>Maximum allowable labor rate per type, for compliance flagging.</summary>
    public Dictionary<string, decimal> MaxRates { get; set; } = new();

    /// <summary>DRP compliance checklist — items the estimator must verify before submitting.</summary>
    public List<DRPChecklistItem> ChecklistItems { get; set; } = new();

    public bool IsExample { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InsurerProfileService
{
    private static InsurerProfileService? _instance;
    public static InsurerProfileService Instance => _instance ??= new InsurerProfileService();

    private readonly string _configPath;
    private List<InsurerProfile> _profiles = new();

    public event EventHandler? ProfilesChanged;

    private InsurerProfileService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop");
        Directory.CreateDirectory(appDataDir);
        _configPath = Path.Combine(appDataDir, "InsurerProfiles.json");
        Load();

        // Seed example profile if empty
        if (_profiles.Count == 0)
            SeedExampleProfile();
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(_configPath)) return;
        try
        {
            var json = File.ReadAllText(_configPath);
            var list = JsonSerializer.Deserialize<List<InsurerProfile>>(json, JsonOpts());
            if (list != null)
                _profiles = list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsurerProfiles] Load error: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_profiles, JsonOpts());
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsurerProfiles] Save error: {ex.Message}");
        }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public List<InsurerProfile> GetAll() => _profiles.OrderBy(p => p.CompanyName).ToList();

    public InsurerProfile? GetById(string id) => _profiles.FirstOrDefault(p => p.Id == id);

    public InsurerProfile Add(string companyName, string programName)
    {
        var profile = new InsurerProfile
        {
            CompanyName = companyName,
            ProgramName = programName
        };
        _profiles.Add(profile);
        Save();
        return profile;
    }

    public void Update(InsurerProfile profile)
    {
        var idx = _profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
            _profiles[idx] = profile;
        else
            _profiles.Add(profile);
        Save();
    }

    public bool Remove(string id)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == id);
        if (profile == null || profile.IsExample) return false;
        _profiles.Remove(profile);
        Save();
        return true;
    }

    // ── Compliance check ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of compliance flags for the given profile and OCR text.
    /// Each entry is (Item, IsMet, Detail).
    /// </summary>
    public List<(DRPChecklistItem Item, bool IsMet, string Detail)> CheckCompliance(
        InsurerProfile profile, string estimateText)
    {
        var results = new List<(DRPChecklistItem, bool, string)>();
        var lower = estimateText.ToLowerInvariant();

        foreach (var item in profile.ChecklistItems)
        {
            bool isMet = false;
            string detail = "";

            if (!string.IsNullOrWhiteSpace(item.EstimateKeywords))
            {
                var keywords = item.EstimateKeywords
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                isMet = keywords.Any(k => lower.Contains(k.ToLowerInvariant()));
                detail = isMet ? "Found in estimate" : "Not detected in estimate";
            }
            else
            {
                // No auto-check: treat as manual — always show as "verify manually"
                isMet = false;
                detail = "Verify manually";
            }

            results.Add((item, isMet, detail));
        }

        return results;
    }

    // ── Example profile ────────────────────────────────────────────────────────

    private void SeedExampleProfile()
    {
        var example = new InsurerProfile
        {
            Id = "example-state-farm-select",
            CompanyName = "State Farm (Example)",
            ProgramName = "Select Service Program",
            ContactName = "DRP Coordinator",
            ContactPhone = "(555) 000-0000",
            ContactEmail = "drp@example.com",
            Notes = "This is an example profile. Edit or delete it and add your real insurer agreements. " +
                    "Rates and rules shown here are illustrative only.",
            IsExample = true,
            AgreedRates = new Dictionary<string, decimal>
            {
                { "Body Labor", 52.00m },
                { "Frame Labor", 52.00m },
                { "Mechanical Labor", 80.00m },
                { "Paint Labor", 52.00m },
                { "Paint Materials", 36.00m }
            },
            MaxRates = new Dictionary<string, decimal>
            {
                { "Body Labor", 54.00m },
                { "Frame Labor", 54.00m },
                { "Mechanical Labor", 85.00m },
                { "Paint Labor", 54.00m },
                { "Paint Materials", 38.00m }
            },
            ChecklistItems = new List<DRPChecklistItem>
            {
                // Documentation
                new DRPChecklistItem
                {
                    Category = "Documentation",
                    Description = "OEM position statements referenced for applicable operations",
                    EstimateKeywords = "oem,position statement",
                    Notes = "Required for all procedures that deviate from standard methods."
                },
                new DRPChecklistItem
                {
                    Category = "Documentation",
                    Description = "Pre/post scan documented on estimate",
                    EstimateKeywords = "pre-scan,post-scan,pre scan,post scan,diagnostic scan",
                    Notes = "Both pre-repair and post-repair scans are required."
                },
                new DRPChecklistItem
                {
                    Category = "Documentation",
                    Description = "Rental authorization noted (if applicable)",
                    EstimateKeywords = "rental,rental car",
                    Notes = "Rental must be pre-authorized by the adjuster."
                },

                // Repair Quality
                new DRPChecklistItem
                {
                    Category = "Repair Quality",
                    Description = "Aluminum repair procedures noted for aluminum panels",
                    EstimateKeywords = "aluminum,aluminium",
                    Notes = "Dedicated aluminum tools and training certification required."
                },
                new DRPChecklistItem
                {
                    Category = "Repair Quality",
                    Description = "ADAS calibration included for windshield or sensor replacements",
                    EstimateKeywords = "adas,calibrat,recalibrat,windshield",
                    Notes = "Required when any ADAS component is disturbed."
                },
                new DRPChecklistItem
                {
                    Category = "Repair Quality",
                    Description = "Blend panels noted when color matching required",
                    EstimateKeywords = "blend",
                    Notes = "Blend adjacent panels when a color match cannot be guaranteed."
                },

                // Parts
                new DRPChecklistItem
                {
                    Category = "Parts",
                    Description = "OEM/LKQ parts used (no aftermarket without prior approval)",
                    EstimateKeywords = "oem,oe,lkq,remanufactured",
                    Notes = "Aftermarket structural parts require written adjuster approval."
                },
                new DRPChecklistItem
                {
                    Category = "Parts",
                    Description = "Sublet operations have supporting invoices on file",
                    EstimateKeywords = "sublet",
                    Notes = "Invoices must be retained for 5 years."
                },

                // Billing
                new DRPChecklistItem
                {
                    Category = "Billing",
                    Description = "Labor rates match agreed DRP rates",
                    // No keyword auto-check — user must verify rates manually
                    EstimateKeywords = null,
                    Notes = $"Body/Frame/Paint: $52/hr | Mechanical: $80/hr | Materials: $36/hr"
                },
                new DRPChecklistItem
                {
                    Category = "Billing",
                    Description = "Paint materials within agreed cap",
                    EstimateKeywords = null,
                    Notes = "Cap: $36.00/hr. Flag anything above before submitting."
                },
                new DRPChecklistItem
                {
                    Category = "Billing",
                    Description = "Storage charges approved by adjuster if applicable",
                    EstimateKeywords = "storage",
                    Notes = "Storage requires advance written authorization."
                }
            }
        };

        _profiles.Add(example);
        Save();
    }

    private static JsonSerializerOptions JsonOpts() => new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
