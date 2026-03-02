#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

/// <summary>
/// Service for managing shop stock parts inventory and invoicing.
/// Allows shops to maintain a parts list and bill customers for stock parts.
/// </summary>
public class ShopStockService
{
    private static ShopStockService? _instance;
    public static ShopStockService Instance => _instance ??= new ShopStockService();

    private ShopStockData _data;
    private readonly string _partsPath;
    private readonly string _settingsPath;

    public event EventHandler? DataChanged;

    private ShopStockService()
    {
        _partsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ShopStockParts.json");
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ShopStockSettings.json");
        _data = new ShopStockData();
        LoadParts();
        LoadSettings();
    }

    #region Data Loading

    private void LoadParts()
    {
        try
        {
            if (File.Exists(_partsPath))
            {
                var json = File.ReadAllText(_partsPath);
                var parts = JsonSerializer.Deserialize<List<StockPart>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parts != null)
                {
                    _data.Parts = parts;
                    System.Diagnostics.Debug.WriteLine($"[ShopStock] Loaded {parts.Count} stock parts");
                }
            }
            else
            {
                // Create sample parts
                _data.Parts = GetSampleParts();
                SaveParts();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShopStock] Error loading parts: {ex.Message}");
        }
    }

    private List<StockPart> GetSampleParts()
    {
        return new List<StockPart>
        {
            // Fasteners & Hardware
            new() { PartNumber = "CLIP-001", Description = "Door Panel Clip - Universal", Category = "Fasteners", CostPrice = 0.50m, SellPrice = 2.00m, QuantityInStock = 100 },
            new() { PartNumber = "CLIP-002", Description = "Fender Liner Clip - Universal", Category = "Fasteners", CostPrice = 0.45m, SellPrice = 1.75m, QuantityInStock = 100 },
            new() { PartNumber = "CLIP-003", Description = "Bumper Cover Clip", Category = "Fasteners", CostPrice = 0.60m, SellPrice = 2.25m, QuantityInStock = 75 },
            new() { PartNumber = "CLIP-004", Description = "Splash Shield Clip", Category = "Fasteners", CostPrice = 0.40m, SellPrice = 1.50m, QuantityInStock = 100 },
            new() { PartNumber = "CLIP-005", Description = "Grille Clip", Category = "Fasteners", CostPrice = 0.55m, SellPrice = 2.00m, QuantityInStock = 50 },
            new() { PartNumber = "BOLT-001", Description = "Bumper Bolt 10mm", Category = "Fasteners", CostPrice = 0.75m, SellPrice = 2.50m, QuantityInStock = 50 },
            new() { PartNumber = "BOLT-002", Description = "Fender Bolt 8mm", Category = "Fasteners", CostPrice = 0.65m, SellPrice = 2.25m, QuantityInStock = 50 },
            new() { PartNumber = "NUT-001", Description = "Push Nut - Universal", Category = "Fasteners", CostPrice = 0.30m, SellPrice = 1.25m, QuantityInStock = 100 },

            // Body Parts
            new() { PartNumber = "MOLD-001", Description = "Door Edge Guard - Black", Category = "Moldings", CostPrice = 8.00m, SellPrice = 25.00m, QuantityInStock = 10 },
            new() { PartNumber = "MOLD-002", Description = "Wheel Well Molding - Universal", Category = "Moldings", CostPrice = 12.00m, SellPrice = 35.00m, QuantityInStock = 8 },
            new() { PartNumber = "SEAL-001", Description = "Door Weatherstrip - Universal", Category = "Seals", CostPrice = 15.00m, SellPrice = 45.00m, QuantityInStock = 6 },
            new() { PartNumber = "SEAL-002", Description = "Window Channel Seal", Category = "Seals", CostPrice = 10.00m, SellPrice = 30.00m, QuantityInStock = 8 },

            // Adhesives & Tape
            new() { PartNumber = "TAPE-001", Description = "3M Double-Sided Tape 1/2\"", Category = "Adhesives", CostPrice = 8.00m, SellPrice = 18.00m, QuantityInStock = 20 },
            new() { PartNumber = "TAPE-002", Description = "3M Double-Sided Tape 1\"", Category = "Adhesives", CostPrice = 12.00m, SellPrice = 25.00m, QuantityInStock = 15 },
            new() { PartNumber = "ADH-001", Description = "Panel Bond Adhesive", Category = "Adhesives", CostPrice = 25.00m, SellPrice = 55.00m, QuantityInStock = 10 },
            new() { PartNumber = "ADH-002", Description = "Windshield Urethane", Category = "Adhesives", CostPrice = 35.00m, SellPrice = 75.00m, QuantityInStock = 6 },

            // Refinish Supplies
            new() { PartNumber = "SAND-080", Description = "Sandpaper 80 Grit (sheet)", Category = "Refinish", CostPrice = 0.50m, SellPrice = 1.50m, QuantityInStock = 100 },
            new() { PartNumber = "SAND-180", Description = "Sandpaper 180 Grit (sheet)", Category = "Refinish", CostPrice = 0.50m, SellPrice = 1.50m, QuantityInStock = 100 },
            new() { PartNumber = "SAND-320", Description = "Sandpaper 320 Grit (sheet)", Category = "Refinish", CostPrice = 0.55m, SellPrice = 1.75m, QuantityInStock = 100 },
            new() { PartNumber = "SAND-600", Description = "Sandpaper 600 Grit (sheet)", Category = "Refinish", CostPrice = 0.60m, SellPrice = 2.00m, QuantityInStock = 100 },
            new() { PartNumber = "MASK-001", Description = "Masking Tape 3/4\"", Category = "Refinish", CostPrice = 4.00m, SellPrice = 10.00m, QuantityInStock = 24 },
            new() { PartNumber = "MASK-002", Description = "Masking Paper 18\"", Category = "Refinish", CostPrice = 8.00m, SellPrice = 18.00m, QuantityInStock = 12 },

            // Misc
            new() { PartNumber = "MISC-001", Description = "Touch-Up Paint Pen", Category = "Miscellaneous", CostPrice = 8.00m, SellPrice = 20.00m, QuantityInStock = 15 },
            new() { PartNumber = "MISC-002", Description = "Spray Can Primer", Category = "Miscellaneous", CostPrice = 6.00m, SellPrice = 15.00m, QuantityInStock = 12 },
            new() { PartNumber = "MISC-003", Description = "Rubbing Compound 16oz", Category = "Miscellaneous", CostPrice = 12.00m, SellPrice = 28.00m, QuantityInStock = 8 }
        };
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ShopStockSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (settings != null)
                {
                    _data.Settings = settings;
                }
            }
            else
            {
                _data.Settings = new ShopStockSettings
                {
                    DefaultTaxRate = 6.625m,
                    ShopName = "Your Shop Name",
                    ShopAddress = "123 Main Street",
                    ShopCity = "City, State ZIP",
                    ShopPhone = "(555) 123-4567"
                };
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShopStock] Error loading settings: {ex.Message}");
            _data.Settings = new ShopStockSettings();
        }
    }

    public void SaveParts()
    {
        try
        {
            var dir = Path.GetDirectoryName(_partsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data.Parts, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_partsPath, json);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShopStock] Error saving parts: {ex.Message}");
        }
    }

    public void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data.Settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsPath, json);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShopStock] Error saving settings: {ex.Message}");
        }
    }

    #endregion

    #region Parts Management

    public List<StockPart> GetAllParts() => _data.Parts;

    public List<StockPart> SearchParts(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _data.Parts;

        var term = searchTerm.ToLowerInvariant();
        return _data.Parts
            .Where(p => (p.PartNumber?.ToLowerInvariant().Contains(term) ?? false) ||
                        (p.Description?.ToLowerInvariant().Contains(term) ?? false) ||
                        (p.Category?.ToLowerInvariant().Contains(term) ?? false))
            .ToList();
    }

    public List<StockPart> GetPartsByCategory(string category)
    {
        if (category == "All" || string.IsNullOrEmpty(category))
            return _data.Parts;

        return _data.Parts
            .Where(p => p.Category == category)
            .ToList();
    }

    public List<string> GetCategories()
    {
        var categories = _data.Parts
            .Select(p => p.Category ?? "Uncategorized")
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        categories.Insert(0, "All");
        return categories;
    }

    public StockPart? GetPart(string partNumber)
    {
        return _data.Parts.FirstOrDefault(p =>
            p.PartNumber?.Equals(partNumber, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public void AddPart(StockPart part)
    {
        _data.Parts.Add(part);
        SaveParts();
    }

    public void UpdatePart(StockPart part)
    {
        var existing = GetPart(part.PartNumber ?? "");
        if (existing != null)
        {
            existing.Description = part.Description;
            existing.Category = part.Category;
            existing.CostPrice = part.CostPrice;
            existing.SellPrice = part.SellPrice;
            existing.QuantityInStock = part.QuantityInStock;
            SaveParts();
        }
    }

    public void DeletePart(string partNumber)
    {
        _data.Parts.RemoveAll(p =>
            p.PartNumber?.Equals(partNumber, StringComparison.OrdinalIgnoreCase) ?? false);
        SaveParts();
    }

    #endregion

    #region Invoice & Settings

    public ShopStockSettings GetSettings() => _data.Settings;

    public void UpdateSettings(ShopStockSettings settings)
    {
        _data.Settings = settings;
        SaveSettings();
    }

    public StockInvoice CreateInvoice()
    {
        return new StockInvoice
        {
            InvoiceNumber = GenerateInvoiceNumber(),
            Date = DateTime.Now,
            ShopName = _data.Settings.ShopName,
            ShopAddress = _data.Settings.ShopAddress,
            ShopCity = _data.Settings.ShopCity,
            ShopPhone = _data.Settings.ShopPhone,
            TaxRate = _data.Settings.DefaultTaxRate,
            Items = new List<StockInvoiceItem>()
        };
    }

    private string GenerateInvoiceNumber()
    {
        var date = DateTime.Now;
        var random = new Random().Next(1, 999);
        return $"STK-{date:yyMMdd}-{random:D3}";
    }

    public void AddItemToInvoice(StockInvoice invoice, StockPart part, int quantity = 1)
    {
        var existing = invoice.Items.FirstOrDefault(i => i.PartNumber == part.PartNumber);
        if (existing != null)
        {
            existing.Quantity += quantity;
            existing.LineTotal = existing.UnitPrice * existing.Quantity;
        }
        else
        {
            invoice.Items.Add(new StockInvoiceItem
            {
                PartNumber = part.PartNumber ?? "",
                Description = part.Description ?? "",
                Quantity = quantity,
                UnitPrice = part.SellPrice,
                LineTotal = part.SellPrice * quantity
            });
        }
    }

    public decimal CalculateSubtotal(StockInvoice invoice)
    {
        return invoice.Items.Sum(i => i.LineTotal);
    }

    public decimal CalculateTax(StockInvoice invoice)
    {
        return CalculateSubtotal(invoice) * (invoice.TaxRate / 100m);
    }

    public decimal CalculateTotal(StockInvoice invoice)
    {
        return CalculateSubtotal(invoice) + CalculateTax(invoice);
    }

    #endregion
}

#region Data Models

public class ShopStockData
{
    public List<StockPart> Parts { get; set; } = new();
    public ShopStockSettings Settings { get; set; } = new();
}

public class StockPart
{
    [JsonPropertyName("partNumber")]
    public string? PartNumber { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("costPrice")]
    public decimal CostPrice { get; set; }

    [JsonPropertyName("sellPrice")]
    public decimal SellPrice { get; set; }

    [JsonPropertyName("quantityInStock")]
    public int QuantityInStock { get; set; }
}

public class ShopStockSettings
{
    public decimal DefaultTaxRate { get; set; } = 6.625m;
    public string ShopName { get; set; } = "";
    public string ShopAddress { get; set; } = "";
    public string ShopCity { get; set; } = "";
    public string ShopPhone { get; set; } = "";
}

public class StockInvoice
{
    public string InvoiceNumber { get; set; } = "";
    public DateTime Date { get; set; }
    public string ShopName { get; set; } = "";
    public string ShopAddress { get; set; } = "";
    public string ShopCity { get; set; } = "";
    public string ShopPhone { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public decimal TaxRate { get; set; }
    public List<StockInvoiceItem> Items { get; set; } = new();
}

public class StockInvoiceItem
{
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

#endregion
