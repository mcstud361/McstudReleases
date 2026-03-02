#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Labor Rates Service - Manages dealer/shop labor rates
    /// Data is stored in AppData/Local to persist through app updates
    /// </summary>
    public class LaborRatesService
    {
        private static LaborRatesService? _instance;
        public static LaborRatesService Instance => _instance ??= new LaborRatesService();

        private LaborRatesData _data;
        private readonly string _dataFilePath;

        public event EventHandler? DataChanged;

        private LaborRatesService()
        {
            // Store in AppData/Local/McStudDesktop so it survives updates
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McStudDesktop");

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _dataFilePath = Path.Combine(appDataPath, "LaborRates.json");
            _data = LoadData();

            System.Diagnostics.Debug.WriteLine($"[LaborRates] Data file: {_dataFilePath}");
            System.Diagnostics.Debug.WriteLine($"[LaborRates] Loaded {_data.Dealers.Count} dealers");
        }

        private LaborRatesData LoadData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var data = JsonSerializer.Deserialize<LaborRatesData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return data ?? new LaborRatesData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaborRates] Error loading: {ex.Message}");
            }

            return new LaborRatesData();
        }

        private void SaveData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_dataFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[LaborRates] Saved {_data.Dealers.Count} dealers");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaborRates] Error saving: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all dealers
        /// </summary>
        public List<DealerLaborRate> GetAllDealers()
        {
            return _data.Dealers.OrderBy(d => d.DealerName).ToList();
        }

        /// <summary>
        /// Search dealers by name or manufacturer
        /// </summary>
        public List<DealerLaborRate> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAllDealers();

            var queryLower = query.ToLowerInvariant();
            return _data.Dealers
                .Where(d =>
                    (d.DealerName?.ToLowerInvariant().Contains(queryLower) == true) ||
                    (d.Manufacturer?.ToLowerInvariant().Contains(queryLower) == true) ||
                    (d.Notes?.ToLowerInvariant().Contains(queryLower) == true))
                .OrderBy(d => d.DealerName)
                .ToList();
        }

        /// <summary>
        /// Get dealers by manufacturer
        /// </summary>
        public List<DealerLaborRate> GetByManufacturer(string manufacturer)
        {
            return _data.Dealers
                .Where(d => d.Manufacturer?.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(d => d.DealerName)
                .ToList();
        }

        /// <summary>
        /// Get unique manufacturers for filtering
        /// </summary>
        public List<string> GetManufacturers()
        {
            return _data.Dealers
                .Where(d => !string.IsNullOrEmpty(d.Manufacturer))
                .Select(d => d.Manufacturer!)
                .Distinct()
                .OrderBy(m => m)
                .ToList();
        }

        /// <summary>
        /// Add a new dealer
        /// </summary>
        public void AddDealer(DealerLaborRate dealer)
        {
            dealer.Id = Guid.NewGuid().ToString();
            dealer.DateAdded = DateTime.Now;
            dealer.DateUpdated = DateTime.Now;

            _data.Dealers.Add(dealer);
            SaveData();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Update an existing dealer
        /// </summary>
        public void UpdateDealer(DealerLaborRate dealer)
        {
            var existing = _data.Dealers.FirstOrDefault(d => d.Id == dealer.Id);
            if (existing != null)
            {
                existing.DealerName = dealer.DealerName;
                existing.Manufacturer = dealer.Manufacturer;
                existing.BodyLaborRate = dealer.BodyLaborRate;
                existing.MechLaborRate = dealer.MechLaborRate;
                existing.PaintLaborRate = dealer.PaintLaborRate;
                existing.FrameLaborRate = dealer.FrameLaborRate;
                existing.GlassLaborRate = dealer.GlassLaborRate;
                existing.Phone = dealer.Phone;
                existing.Notes = dealer.Notes;
                existing.DateUpdated = DateTime.Now;

                SaveData();
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Delete a dealer
        /// </summary>
        public void DeleteDealer(string id)
        {
            var dealer = _data.Dealers.FirstOrDefault(d => d.Id == id);
            if (dealer != null)
            {
                _data.Dealers.Remove(dealer);
                SaveData();
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Export data as JSON (for backup/sharing)
        /// </summary>
        public string ExportAsJson()
        {
            return JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        /// <summary>
        /// Import data from JSON
        /// </summary>
        public int ImportFromJson(string json, bool replaceExisting = false)
        {
            try
            {
                var imported = JsonSerializer.Deserialize<LaborRatesData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (imported?.Dealers == null) return 0;

                if (replaceExisting)
                {
                    _data = imported;
                }
                else
                {
                    // Merge - add only new dealers
                    foreach (var dealer in imported.Dealers)
                    {
                        var exists = _data.Dealers.Any(d =>
                            d.DealerName?.Equals(dealer.DealerName, StringComparison.OrdinalIgnoreCase) == true &&
                            d.Manufacturer?.Equals(dealer.Manufacturer, StringComparison.OrdinalIgnoreCase) == true);

                        if (!exists)
                        {
                            dealer.Id = Guid.NewGuid().ToString();
                            dealer.DateAdded = DateTime.Now;
                            _data.Dealers.Add(dealer);
                        }
                    }
                }

                SaveData();
                DataChanged?.Invoke(this, EventArgs.Empty);
                return imported.Dealers.Count;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaborRates] Import error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get the data file path (for user reference)
        /// </summary>
        public string GetDataFilePath() => _dataFilePath;
    }

    #region Data Models

    public class LaborRatesData
    {
        public List<DealerLaborRate> Dealers { get; set; } = new();
    }

    public class DealerLaborRate
    {
        public string? Id { get; set; }
        public string? DealerName { get; set; }
        public string? Manufacturer { get; set; }
        public decimal BodyLaborRate { get; set; }
        public decimal MechLaborRate { get; set; }
        public decimal PaintLaborRate { get; set; }
        public decimal FrameLaborRate { get; set; }
        public decimal GlassLaborRate { get; set; }
        public string? Phone { get; set; }
        public string? Notes { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateUpdated { get; set; }
    }

    #endregion
}
