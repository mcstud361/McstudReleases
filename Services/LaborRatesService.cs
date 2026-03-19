#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Dealer Information Service - Manages dealer contacts, labor rates, and parts info
    /// Data is stored in AppData/Local to persist through app updates
    /// </summary>
    public class LaborRatesService
    {
        private static LaborRatesService? _instance;
        public static LaborRatesService Instance => _instance ??= new LaborRatesService();

        private LaborRatesData _data;
        private readonly string _dataFilePath;
        private readonly string _legacyFilePath;

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

            _dataFilePath = Path.Combine(appDataPath, "DealerInfo.json");
            _legacyFilePath = Path.Combine(appDataPath, "LaborRates.json");

            // Migrate old file if new one doesn't exist
            if (!File.Exists(_dataFilePath) && File.Exists(_legacyFilePath))
            {
                try
                {
                    File.Copy(_legacyFilePath, _dataFilePath);
                    System.Diagnostics.Debug.WriteLine("[LaborRates] Migrated LaborRates.json → DealerInfo.json");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LaborRates] Migration error: {ex.Message}");
                }
            }

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

                    if (data != null)
                    {
                        // Legacy migration: move old single Phone field into PhoneNumbers list
                        foreach (var dealer in data.Dealers)
                        {
                            if (!string.IsNullOrEmpty(dealer.Phone) && dealer.PhoneNumbers.Count == 0)
                            {
                                dealer.PhoneNumbers.Add(dealer.Phone);
                                dealer.Phone = null;
                            }
                        }
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaborRates] Error loading: {ex.Message}");
            }

            // First run - seed example dealer
            var freshData = new LaborRatesData();
            freshData.Dealers.Add(CreateExampleDealer());
            return freshData;
        }

        private DealerLaborRate CreateExampleDealer()
        {
            return new DealerLaborRate
            {
                Id = Guid.NewGuid().ToString(),
                DealerName = "Example Ford Dealership",
                Manufacturer = "Ford",
                IsExample = true,
                Address = "1234 Motor Way",
                City = "Springfield",
                State = "IL",
                Zip = "62701",
                PhoneNumbers = new List<string> { "(555) 123-4567", "(555) 123-4568" },
                Emails = new List<string> { "parts@exampleford.com" },
                Contacts = new List<DealerContact>
                {
                    new DealerContact
                    {
                        Name = "John Smith",
                        Role = "Parts Manager",
                        PhoneNumbers = new List<string> { "(555) 123-4568" },
                        Emails = new List<string> { "john@exampleford.com" }
                    },
                    new DealerContact
                    {
                        Name = "Jane Doe",
                        Role = "Delivery Driver",
                        PhoneNumbers = new List<string> { "(555) 123-4569" },
                        Emails = new List<string>()
                    }
                },
                PartsDiscountPercent = 25,
                Returns = new ReturnsInfo { AcceptsReturns = true, ReturnsNote = "Within 30 days with receipt" },
                Delivery = new DeliveryInfo
                {
                    DeliversParts = true,
                    DeliveryNote = "Mon-Fri only",
                    DeliveryTime = "10:00 AM",
                    RunsPerDay = 2
                },
                MechLaborRate = 85.00m,
                BodyLaborRate = 72.00m,
                PaintLaborRate = 68.00m,
                FrameLaborRate = 90.00m,
                GlassLaborRate = 65.00m,
                Notes = "This is an example dealer to show how the feature works. Feel free to edit or delete it.",
                DateAdded = DateTime.Now,
                DateUpdated = DateTime.Now
            };
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
        /// Search dealers by name, manufacturer, address, phone, email, or contact info
        /// </summary>
        public List<DealerLaborRate> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAllDealers();

            var q = query.ToLowerInvariant();
            return _data.Dealers
                .Where(d =>
                    Contains(d.DealerName, q) ||
                    Contains(d.Manufacturer, q) ||
                    Contains(d.Notes, q) ||
                    Contains(d.Address, q) ||
                    Contains(d.City, q) ||
                    Contains(d.State, q) ||
                    d.PhoneNumbers.Any(p => p.ToLowerInvariant().Contains(q)) ||
                    d.Emails.Any(e => e.ToLowerInvariant().Contains(q)) ||
                    d.Contacts.Any(c =>
                        Contains(c.Name, q) ||
                        Contains(c.Role, q) ||
                        c.PhoneNumbers.Any(p => p.ToLowerInvariant().Contains(q)) ||
                        c.Emails.Any(e => e.ToLowerInvariant().Contains(q))))
                .OrderBy(d => d.DealerName)
                .ToList();
        }

        private static bool Contains(string? value, string query)
        {
            return value?.ToLowerInvariant().Contains(query) == true;
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
                existing.Address = dealer.Address;
                existing.City = dealer.City;
                existing.State = dealer.State;
                existing.Zip = dealer.Zip;
                existing.PhoneNumbers = dealer.PhoneNumbers;
                existing.Emails = dealer.Emails;
                existing.Contacts = dealer.Contacts;
                existing.PartsDiscountPercent = dealer.PartsDiscountPercent;
                existing.Returns = dealer.Returns;
                existing.Delivery = dealer.Delivery;
                existing.BodyLaborRate = dealer.BodyLaborRate;
                existing.MechLaborRate = dealer.MechLaborRate;
                existing.PaintLaborRate = dealer.PaintLaborRate;
                existing.FrameLaborRate = dealer.FrameLaborRate;
                existing.GlassLaborRate = dealer.GlassLaborRate;
                existing.Phone = dealer.Phone;
                existing.Notes = dealer.Notes;
                existing.IsExample = dealer.IsExample;
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

    public class DealerContact
    {
        public string? Name { get; set; }
        public string? Role { get; set; }
        public List<string> PhoneNumbers { get; set; } = new();
        public List<string> Emails { get; set; } = new();
    }

    public class DeliveryInfo
    {
        public bool DeliversParts { get; set; }
        public string? DeliveryNote { get; set; }
        public string? DeliveryTime { get; set; }
        public int RunsPerDay { get; set; }
    }

    public class ReturnsInfo
    {
        public bool AcceptsReturns { get; set; }
        public string? ReturnsNote { get; set; }
    }

    public class DealerLaborRate
    {
        public string? Id { get; set; }
        public string? DealerName { get; set; }
        public string? Manufacturer { get; set; }
        public bool IsExample { get; set; }

        // Location
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        // Contact info (dealer-level)
        public List<string> PhoneNumbers { get; set; } = new();
        public List<string> Emails { get; set; } = new();

        // Contact people
        public List<DealerContact> Contacts { get; set; } = new();

        // Parts info
        public decimal PartsDiscountPercent { get; set; }
        public ReturnsInfo Returns { get; set; } = new();
        public DeliveryInfo Delivery { get; set; } = new();

        // Labor rates
        public decimal BodyLaborRate { get; set; }
        public decimal MechLaborRate { get; set; }
        public decimal PaintLaborRate { get; set; }
        public decimal FrameLaborRate { get; set; }
        public decimal GlassLaborRate { get; set; }

        // Legacy field - kept for deserialization of old data
        public string? Phone { get; set; }

        public string? Notes { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateUpdated { get; set; }
    }

    #endregion
}
