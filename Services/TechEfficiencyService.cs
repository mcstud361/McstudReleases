#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services
{
    public enum TechTier
    {
        A,
        B,
        C
    }

    public class Technician
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public decimal DefaultHoursPerDay { get; set; } = 8m;
        public TechTier Tier { get; set; } = TechTier.B;
        public decimal EfficiencyPercent { get; set; } = 100m;
    }

    public class TechEfficiencyEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TechnicianId { get; set; } = string.Empty;
        public string? RONumber { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public decimal BilledHours { get; set; }
        public decimal ActualHours { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class TechEfficiencyData
    {
        public List<Technician> Technicians { get; set; } = new();
        public List<TechEfficiencyEntry> Entries { get; set; } = new();
    }

    public class TechEfficiencySummary
    {
        public decimal TotalBilledHours { get; set; }
        public decimal TotalActualHours { get; set; }
        public decimal TotalAvailableHours { get; set; }
        public decimal EfficiencyPercent { get; set; }
        public decimal ProductivityPercent { get; set; }
        public int EntryCount { get; set; }
    }

    public class TechEfficiencyService
    {
        private static TechEfficiencyService? _instance;
        public static TechEfficiencyService Instance => _instance ??= new TechEfficiencyService();

        private readonly string _dataPath;
        private TechEfficiencyData _data;

        public event EventHandler? DataChanged;

        private TechEfficiencyService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "McStudDesktop");
            Directory.CreateDirectory(appDir);
            _dataPath = Path.Combine(appDir, "TechEfficiency.json");
            _data = LoadData();
        }

        // --- Technician CRUD ---

        public List<Technician> GetTechnicians() => _data.Technicians.ToList();

        public Technician AddTechnician(string name, TechTier tier = TechTier.B, decimal? efficiencyPercent = null, decimal defaultHoursPerDay = 8m)
        {
            var tech = new Technician
            {
                Name = name.Trim(),
                Tier = tier,
                EfficiencyPercent = efficiencyPercent ?? GetDefaultEfficiency(tier),
                DefaultHoursPerDay = defaultHoursPerDay
            };
            _data.Technicians.Add(tech);
            SaveData();
            return tech;
        }

        public void UpdateTechnician(string id, string? name = null, TechTier? tier = null, decimal? efficiencyPercent = null, decimal? defaultHoursPerDay = null)
        {
            var tech = _data.Technicians.FirstOrDefault(t => t.Id == id);
            if (tech == null) return;
            if (name != null) tech.Name = name.Trim();
            if (tier.HasValue) tech.Tier = tier.Value;
            if (efficiencyPercent.HasValue) tech.EfficiencyPercent = efficiencyPercent.Value;
            if (defaultHoursPerDay.HasValue) tech.DefaultHoursPerDay = defaultHoursPerDay.Value;
            SaveData();
        }

        public void RemoveTechnician(string id)
        {
            _data.Technicians.RemoveAll(t => t.Id == id);
            _data.Entries.RemoveAll(e => e.TechnicianId == id);
            SaveData();
        }

        // --- Entry CRUD ---

        public List<TechEfficiencyEntry> GetEntries(string? technicianId = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _data.Entries.AsEnumerable();
            if (technicianId != null)
                query = query.Where(e => e.TechnicianId == technicianId);
            if (from.HasValue)
                query = query.Where(e => e.Date >= from.Value.Date);
            if (to.HasValue)
                query = query.Where(e => e.Date <= to.Value.Date);
            return query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ToList();
        }

        public TechEfficiencyEntry AddEntry(string technicianId, decimal billedHours, decimal actualHours,
            DateTime? date = null, string? roNumber = null, string? notes = null)
        {
            var entry = new TechEfficiencyEntry
            {
                TechnicianId = technicianId,
                BilledHours = billedHours,
                ActualHours = actualHours,
                Date = date ?? DateTime.Today,
                RONumber = roNumber?.Trim(),
                Notes = notes?.Trim()
            };
            _data.Entries.Add(entry);
            SaveData();
            return entry;
        }

        public void RemoveEntry(string id)
        {
            _data.Entries.RemoveAll(e => e.Id == id);
            SaveData();
        }

        // --- Calculations ---

        public static decimal CalculateEfficiency(decimal billedHours, decimal actualHours)
        {
            if (actualHours <= 0) return 0;
            return Math.Round(billedHours / actualHours * 100, 1);
        }

        public static decimal CalculateProductivity(decimal totalBilledHours, decimal totalAvailableHours)
        {
            if (totalAvailableHours <= 0) return 0;
            return Math.Round(totalBilledHours / totalAvailableHours * 100, 1);
        }

        public static string GetBenchmarkLabel(decimal efficiencyPercent)
        {
            return efficiencyPercent switch
            {
                >= 125 => "Top Tier",
                >= 100 => "Above Average",
                >= 80 => "Average",
                _ => "Needs Improvement"
            };
        }

        public static decimal GetDefaultEfficiency(TechTier tier)
        {
            return tier switch
            {
                TechTier.A => 150m,
                TechTier.B => 100m,
                TechTier.C => 75m,
                _ => 100m
            };
        }

        public static decimal CalculateExpectedHours(decimal bookHours, decimal efficiencyPercent)
        {
            if (efficiencyPercent <= 0) return 0;
            return Math.Round(bookHours / (efficiencyPercent / 100m), 1);
        }

        public TechEfficiencySummary GetSummary(string technicianId, DateTime from, DateTime to)
        {
            var entries = GetEntries(technicianId, from, to);
            var tech = _data.Technicians.FirstOrDefault(t => t.Id == technicianId);
            var hoursPerDay = tech?.DefaultHoursPerDay ?? 8m;

            var totalBilled = entries.Sum(e => e.BilledHours);
            var totalActual = entries.Sum(e => e.ActualHours);

            // Count business days in range for available hours
            var businessDays = 0;
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                    businessDays++;
            }
            var totalAvailable = hoursPerDay * businessDays;

            return new TechEfficiencySummary
            {
                TotalBilledHours = totalBilled,
                TotalActualHours = totalActual,
                TotalAvailableHours = totalAvailable,
                EfficiencyPercent = CalculateEfficiency(totalBilled, totalActual),
                ProductivityPercent = CalculateProductivity(totalBilled, totalAvailable),
                EntryCount = entries.Count
            };
        }

        // --- Persistence ---

        private void SaveData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataPath, json);
                DataChanged?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("[TechEfficiency] Data saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TechEfficiency] Error saving: {ex.Message}");
            }
        }

        private TechEfficiencyData LoadData()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var json = File.ReadAllText(_dataPath);
                    var data = JsonSerializer.Deserialize<TechEfficiencyData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (data != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[TechEfficiency] Data loaded");
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TechEfficiency] Error loading, using defaults: {ex.Message}");
            }
            return new TechEfficiencyData();
        }
    }
}
