#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using McstudDesktop.Models;

namespace McStudDesktop.Services
{
    public class SOPBaselineService
    {
        private static readonly Lazy<SOPBaselineService> _instance = new(() => new SOPBaselineService());
        public static SOPBaselineService Instance => _instance.Value;

        private readonly string _filePath;
        private SOPBaselineSettings _settings = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private SOPBaselineService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop"
            );
            Directory.CreateDirectory(appDataPath);
            _filePath = Path.Combine(appDataPath, "sop_baseline_settings.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _settings = JsonSerializer.Deserialize<SOPBaselineSettings>(json, _jsonOptions) ?? new SOPBaselineSettings();
                    if (_settings.Items.Count == 0)
                        _settings.Items = GetDefaultItems();
                }
                else
                {
                    _settings = new SOPBaselineSettings { Items = GetDefaultItems() };
                    Save();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SOPBaselineService] Load error: {ex.Message}");
                _settings = new SOPBaselineSettings { Items = GetDefaultItems() };
            }
        }

        private void Save()
        {
            try
            {
                _settings.LastModified = DateTime.Now;
                var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SOPBaselineService] Save error: {ex.Message}");
            }
        }

        public List<SOPBaselineItem> GetAllItems() => _settings.Items.ToList();

        public List<SOPBaselineItem> GetEnabledItems() => _settings.Items.Where(i => i.IsEnabled).ToList();

        public void SetItemEnabled(string id, bool enabled)
        {
            var item = _settings.Items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                item.IsEnabled = enabled;
                Save();
            }
        }

        public void AddCustomItem(SOPBaselineItem item)
        {
            item.IsCustom = true;
            item.Id = Guid.NewGuid().ToString("N")[..8];
            _settings.Items.Add(item);
            Save();
        }

        public void RemoveCustomItem(string id)
        {
            var item = _settings.Items.FirstOrDefault(i => i.Id == id && i.IsCustom);
            if (item != null)
            {
                _settings.Items.Remove(item);
                Save();
            }
        }

        public void ResetToDefaults()
        {
            _settings.Items = GetDefaultItems();
            Save();
        }

        public void SaveAll()
        {
            Save();
        }

        public static List<SOPBaselineItem> GetDefaultItems()
        {
            return new List<SOPBaselineItem>
            {
                // === Electrical (6) ===
                new() { Id = "elec01", Name = "Disconnect/Reconnect Battery", Section = "Electrical",
                    Description = "Disconnect and reconnect battery for safe electrical work.",
                    WhyNeeded = "Required for any repair involving electrical components or welding.",
                    EstimatedCost = 18m, LaborHours = 0.3m, Severity = "High" },
                new() { Id = "elec02", Name = "Test Battery Condition", Section = "Electrical",
                    Description = "Test battery condition and document results.",
                    WhyNeeded = "Verifies battery health before and after repairs — prevents comebacks.",
                    EstimatedCost = 15m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "elec03", Name = "Electronic Reset", Section = "Electrical",
                    Description = "Reset electronic modules after battery disconnect.",
                    WhyNeeded = "Modules require reinitialization after power loss — window relearn, idle relearn, etc.",
                    EstimatedCost = 25m, LaborHours = 0.3m, Severity = "High" },
                new() { Id = "elec04", Name = "Cover and Protect Electrical", Section = "Electrical",
                    Description = "Cover and protect electrical components during repair.",
                    WhyNeeded = "Prevents water and debris damage to exposed connectors and modules.",
                    EstimatedCost = 12m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "elec05", Name = "Battery Support", Section = "Electrical",
                    Description = "Maintain battery charge with memory saver or charger during repair.",
                    WhyNeeded = "Prevents loss of radio presets, seat memory, and module programming.",
                    EstimatedCost = 15m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "elec06", Name = "Charge and Maintain Battery during ADAS", Section = "Electrical",
                    Description = "Charge and maintain battery during ADAS calibration procedures.",
                    WhyNeeded = "ADAS calibration requires stable voltage — low battery causes calibration failure.",
                    EstimatedCost = 20m, LaborHours = 0.3m, Severity = "High" },

                // === Vehicle Diagnostics (10) ===
                new() { Id = "diag01", Name = "Pre-Scan", Section = "Vehicle Diagnostics",
                    Description = "Diagnostic scan before repairs begin.",
                    WhyNeeded = "Documents pre-existing DTCs and establishes baseline for post-scan comparison.",
                    EstimatedCost = 40m, LaborHours = 0.5m, Severity = "Critical" },
                new() { Id = "diag02", Name = "In-Process Scan", Section = "Vehicle Diagnostics",
                    Description = "Diagnostic scan during repair process.",
                    WhyNeeded = "Verifies systems are responding correctly as repairs progress.",
                    EstimatedCost = 35m, LaborHours = 0.4m, Severity = "Medium" },
                new() { Id = "diag03", Name = "Post Scan", Section = "Vehicle Diagnostics",
                    Description = "Diagnostic scan after all repairs completed.",
                    WhyNeeded = "Verifies all DTCs are cleared and no new codes introduced by repair.",
                    EstimatedCost = 40m, LaborHours = 0.5m, Severity = "Critical" },
                new() { Id = "diag04", Name = "Setup Scan Tool", Section = "Vehicle Diagnostics",
                    Description = "Setup and configure diagnostic scan tool for vehicle.",
                    WhyNeeded = "Scan tool setup time is a billable operation per DEG guidelines.",
                    EstimatedCost = 25m, LaborHours = 0.3m, Severity = "Medium" },
                new() { Id = "diag05", Name = "Dynamic Systems Verification", Section = "Vehicle Diagnostics",
                    Description = "Verify dynamic systems operation (drive cycle / road test).",
                    WhyNeeded = "Required to verify repair completion and clear certain adaptive DTCs.",
                    EstimatedCost = 30m, LaborHours = 0.5m, Severity = "Medium" },
                new() { Id = "diag06", Name = "OEM Research", Section = "Vehicle Diagnostics",
                    Description = "Research OEM repair procedures and position statements.",
                    WhyNeeded = "OEM procedures must be followed for proper repair — billable research time.",
                    EstimatedCost = 40m, LaborHours = 0.5m, Severity = "High" },
                new() { Id = "diag07", Name = "ADAS Diagnostic Report", Section = "Vehicle Diagnostics",
                    Description = "Generate ADAS diagnostic report documenting system status.",
                    WhyNeeded = "Documents ADAS system status pre/post repair for liability and insurance.",
                    EstimatedCost = 50m, LaborHours = 0.5m, Severity = "High" },
                new() { Id = "diag08", Name = "Setup ADAS Equipment", Section = "Vehicle Diagnostics",
                    Description = "Setup ADAS calibration equipment and targets.",
                    WhyNeeded = "ADAS calibration equipment setup is a separate billable operation.",
                    EstimatedCost = 45m, LaborHours = 0.5m, Severity = "High" },
                new() { Id = "diag09", Name = "Simulate Full Fluids", Section = "Vehicle Diagnostics",
                    Description = "Simulate full fluid levels for accurate diagnostics.",
                    WhyNeeded = "Some sensors require proper fluid levels to report correctly during scan.",
                    EstimatedCost = 15m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "diag10", Name = "Check and Adjust Tire Pressure", Section = "Vehicle Diagnostics",
                    Description = "Check and adjust tire pressure for diagnostics and ADAS calibration.",
                    WhyNeeded = "Incorrect tire pressure affects TPMS readings and ADAS calibration accuracy.",
                    EstimatedCost = 12m, LaborHours = 0.2m, Severity = "Medium" },

                // === Miscellaneous (19) ===
                new() { Id = "misc01", Name = "Pre-Wash", Section = "Miscellaneous",
                    Description = "Pre-wash vehicle before repair work.",
                    WhyNeeded = "Ensures clean work surface and prevents contamination of repair areas.",
                    EstimatedCost = 20m, LaborHours = 0.3m, Severity = "Medium" },
                new() { Id = "misc02", Name = "Clean for Delivery", Section = "Miscellaneous",
                    Description = "Final cleaning of vehicle before customer delivery.",
                    WhyNeeded = "Professional delivery standard — removes dust, overspray, and repair debris.",
                    EstimatedCost = 25m, LaborHours = 0.4m, Severity = "Medium" },
                new() { Id = "misc03", Name = "Glass Cleaner", Section = "Miscellaneous",
                    Description = "Clean all glass surfaces after repair.",
                    WhyNeeded = "Overspray and dust settle on glass during repair — must be cleaned for delivery.",
                    EstimatedCost = 8m, LaborHours = 0.2m, Severity = "Low" },
                new() { Id = "misc04", Name = "Mask and Protect", Section = "Miscellaneous",
                    Description = "Mask and protect adjacent panels and trim during repair.",
                    WhyNeeded = "Prevents overspray and damage to non-repair areas.",
                    EstimatedCost = 15m, LaborHours = 0.3m, Severity = "Medium" },
                new() { Id = "misc05", Name = "Parts Disposal", Section = "Miscellaneous",
                    Description = "Dispose of damaged parts removed during repair.",
                    WhyNeeded = "Parts disposal is a billable operation — covers handling and disposal fees.",
                    EstimatedCost = 10m, LaborHours = 0.1m, Severity = "Low" },
                new() { Id = "misc06", Name = "Hazardous Waste", Section = "Miscellaneous",
                    Description = "Hazardous waste disposal for chemicals, fluids, and materials.",
                    WhyNeeded = "EPA-regulated disposal of paint waste, solvents, and contaminated materials.",
                    EstimatedCost = 15m, LaborHours = 0.1m, Severity = "Low" },
                new() { Id = "misc07", Name = "Misc Hardware", Section = "Miscellaneous",
                    Description = "Miscellaneous hardware (clips, fasteners, bolts) used during repair.",
                    WhyNeeded = "Replacement hardware costs are billable — clips, push pins, fasteners break during R&I.",
                    EstimatedCost = 12m, LaborHours = 0m, Severity = "Low" },
                new() { Id = "misc08", Name = "Steering Wheel Cover/Seat Cover/Floor Mat", Section = "Miscellaneous",
                    Description = "Interior protection during repair — seat covers, steering wheel cover, floor mats.",
                    WhyNeeded = "Protects interior from grease, dirt, and damage during repair process.",
                    EstimatedCost = 8m, LaborHours = 0.1m, Severity = "Low" },
                new() { Id = "misc09", Name = "Refinish Material Invoice", Section = "Miscellaneous",
                    Description = "Invoice for refinish materials used (paint, primer, clear coat).",
                    WhyNeeded = "Refinish materials are billed separately from labor — must be documented.",
                    EstimatedCost = 0m, LaborHours = 0m, Severity = "High" },
                new() { Id = "misc10", Name = "Color Tint", Section = "Miscellaneous",
                    Description = "Color tinting to match OEM paint formula.",
                    WhyNeeded = "Paint color matching requires tinting adjustments for proper blend.",
                    EstimatedCost = 20m, LaborHours = 0.3m, Severity = "Medium" },
                new() { Id = "misc11", Name = "Spray Out Cards", Section = "Miscellaneous",
                    Description = "Spray out test cards to verify color match before application.",
                    WhyNeeded = "Test spray confirms color accuracy before painting the vehicle.",
                    EstimatedCost = 10m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "misc12", Name = "Static Gun", Section = "Miscellaneous",
                    Description = "Use static gun to remove dust and particles before clear coat.",
                    WhyNeeded = "Static elimination prevents dust nibs in final finish — improves quality.",
                    EstimatedCost = 5m, LaborHours = 0.1m, Severity = "Low" },
                new() { Id = "misc13", Name = "Touch Up Painted Bolts", Section = "Miscellaneous",
                    Description = "Touch up exposed bolt heads and hardware with matching paint.",
                    WhyNeeded = "Exposed hardware must match surrounding paint for quality finish.",
                    EstimatedCost = 8m, LaborHours = 0.2m, Severity = "Low" },
                new() { Id = "misc14", Name = "Monitor Flash and Cure Time", Section = "Miscellaneous",
                    Description = "Monitor flash and cure times between coats.",
                    WhyNeeded = "Proper flash/cure time prevents defects — billable monitoring time.",
                    EstimatedCost = 15m, LaborHours = 0.3m, Severity = "Medium" },
                new() { Id = "misc15", Name = "Cover Car for Overspray", Section = "Miscellaneous",
                    Description = "Cover entire vehicle to prevent overspray on non-painted areas.",
                    WhyNeeded = "Full vehicle cover prevents overspray contamination on finished surfaces.",
                    EstimatedCost = 12m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "misc16", Name = "Cover for Edging", Section = "Miscellaneous",
                    Description = "Mask and cover for edging operations.",
                    WhyNeeded = "Edging requires precise masking to prevent overspray on adjacent panels.",
                    EstimatedCost = 10m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "misc17", Name = "Mask for Buffing", Section = "Miscellaneous",
                    Description = "Mask adjacent panels and trim before buffing/polishing.",
                    WhyNeeded = "Buffing compound can damage trim and adjacent panels if not masked.",
                    EstimatedCost = 8m, LaborHours = 0.2m, Severity = "Low" },
                new() { Id = "misc18", Name = "Cover Engine Compartment", Section = "Miscellaneous",
                    Description = "Cover engine compartment during repair and refinish.",
                    WhyNeeded = "Prevents overspray and debris from contaminating engine components.",
                    EstimatedCost = 10m, LaborHours = 0.2m, Severity = "Medium" },
                new() { Id = "misc19", Name = "Cover Interior and Jambs for Refinish", Section = "Miscellaneous",
                    Description = "Cover interior surfaces and door jambs during refinish operations.",
                    WhyNeeded = "Protects interior and jambs from overspray during paint application.",
                    EstimatedCost = 12m, LaborHours = 0.2m, Severity = "Medium" },
            };
        }
    }
}
