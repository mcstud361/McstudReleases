#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

/// <summary>
/// Service for managing color tint pricing and invoice generation.
/// Supports multiple pricing tiers: Cost, Shop Price, Customer/Insurance Price
/// </summary>
public class ColorTintService
{
    private static ColorTintService? _instance;
    public static ColorTintService Instance => _instance ??= new ColorTintService();

    private ColorTintData _data;
    private readonly string _dataPath;
    private readonly string _settingsPath;

    public event EventHandler? DataChanged;

    private ColorTintService()
    {
        _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ColorTints.json");
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ColorTintSettings.json");
        _data = new ColorTintData();
        LoadData();
        LoadSettings();
    }

    #region Data Loading

    private void LoadData()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = File.ReadAllText(_dataPath);
                var tints = JsonSerializer.Deserialize<List<ColorTintItem>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tints != null)
                {
                    _data.Tints = tints;
                    System.Diagnostics.Debug.WriteLine($"[ColorTint] Loaded {tints.Count} color tints");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ColorTint] Error loading data: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ColorTintSettings>(json, new JsonSerializerOptions
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
                // Create default settings
                _data.Settings = new ColorTintSettings
                {
                    ShopMarkupPercent = 0, // Shop gets cost price
                    CustomerMarkupPercent = 15, // Customers/Insurance get 15% markup
                    DefaultTaxRate = 6.625m, // NJ sales tax
                    ShopName = "Your Shop Name",
                    ShopAddress = "123 Main Street",
                    ShopCity = "City, State ZIP",
                    ShopPhone = "(555) 123-4567",
                    ShopFax = "(555) 123-4568"
                };
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ColorTint] Error loading settings: {ex.Message}");
            _data.Settings = new ColorTintSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data.Settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsPath, json);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ColorTint] Error saving settings: {ex.Message}");
        }
    }

    public void SaveTints()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data.Tints, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_dataPath, json);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ColorTint] Error saving tints: {ex.Message}");
        }
    }

    #endregion

    #region Tint Management

    public List<ColorTintItem> GetAllTints() => _data.Tints;

    public List<ColorTintItem> SearchTints(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _data.Tints;

        var term = searchTerm.ToLowerInvariant();
        return _data.Tints
            .Where(t => (t.PartNumber?.ToLowerInvariant().Contains(term) ?? false) ||
                        (t.Description?.ToLowerInvariant().Contains(term) ?? false))
            .ToList();
    }

    public List<ColorTintItem> GetTintsByCategory(string category)
    {
        return _data.Tints
            .Where(t => GetCategory(t.PartNumber) == category)
            .ToList();
    }

    public List<string> GetCategories()
    {
        return _data.Tints
            .Select(t => GetCategory(t.PartNumber))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    public static string GetCategory(string? partNumber)
    {
        if (string.IsNullOrEmpty(partNumber)) return "Other";

        // Standox Effect Tints
        if (partNumber.StartsWith("11-LE") || partNumber.StartsWith("11-LR"))
            return "Standox Effect Tints";
        // Standox Pearls/Effects
        if (partNumber.StartsWith("11-E"))
            return "Standox Pearls/Effects";

        // Glasurit Basecoat Tints
        if (partNumber.StartsWith("100-B"))
            return "Basecoat Tints";
        // Glasurit Effect Pearls
        if (partNumber.StartsWith("100-E"))
            return "Effect Pearls";
        // Glasurit Interior Colors
        if (partNumber.StartsWith("100-IC"))
            return "Interior Colors";
        // Glasurit Blending/Additives
        if (partNumber.StartsWith("100-MB") || partNumber.StartsWith("100-MPT") || partNumber.StartsWith("100-M50X"))
            return "Blending/Additives";
        // Glasurit Metallic/Mixing
        if (partNumber.StartsWith("100-M"))
            return "Metallic/Mixing";

        // Tinting Bases (22- series)
        if (partNumber.StartsWith("22-"))
            return "Tinting Bases";
        // Solventborne Color (55- series)
        if (partNumber.StartsWith("55-"))
            return "Solventborne Color";
        // Ready-Mix Color (80- series)
        if (partNumber.StartsWith("80-"))
            return "Ready-Mix Color";
        // Waterborne Basecoat (90- series)
        if (partNumber.StartsWith("90-"))
            return "Waterborne Basecoat";
        // Pearl Effects (93- series)
        if (partNumber.StartsWith("93-"))
            return "Pearl Effects";
        // Special Effects (98- series)
        if (partNumber.StartsWith("98-"))
            return "Special Effects";

        // UV Primers (151- series)
        if (partNumber.StartsWith("151-"))
            return "UV Primers";
        // Primer Surfacer (176- series)
        if (partNumber.StartsWith("176-"))
            return "Primer Surfacer";
        // Etching Primer (283- series)
        if (partNumber.StartsWith("283-"))
            return "Etching Primer";
        // Primers & Sealers (285- series)
        if (partNumber.StartsWith("285-"))
            return "Primers & Sealers";

        // Reducers & Blenders (352- series)
        if (partNumber.StartsWith("352-"))
            return "Reducers & Blenders";
        // Activators (355- series)
        if (partNumber.StartsWith("355-"))
            return "Activators";
        // Metal Cleaner (360- series)
        if (partNumber.StartsWith("360-"))
            return "Cleaners";
        // Additives (522- series)
        if (partNumber.StartsWith("522-"))
            return "Additives";
        // Cleaners (541- series)
        if (partNumber.StartsWith("541-"))
            return "Cleaners";

        // CV Primer (568- series)
        if (partNumber.StartsWith("568-"))
            return "CV Primer";
        // Etch Activator (583- series)
        if (partNumber.StartsWith("583-"))
            return "Activators";
        // Basecoat Activator (590- series)
        if (partNumber.StartsWith("590-"))
            return "Activators";

        // Waterbased Cleaners (700- series)
        if (partNumber.StartsWith("700-"))
            return "Cleaners";
        // Epoxy Primers (801- series)
        if (partNumber.StartsWith("801-"))
            return "Epoxy Primers";
        // Body Fillers (839- series)
        if (partNumber.StartsWith("839-"))
            return "Body Fillers";

        // Clearcoats (923- series)
        if (partNumber.StartsWith("923-"))
            return "Clearcoats";
        // Hardeners (929- series)
        if (partNumber.StartsWith("929-"))
            return "Hardeners";
        // Plastic Primers (934- series)
        if (partNumber.StartsWith("934-"))
            return "Plastic Primers";
        // Hardener Paste (948- series)
        if (partNumber.StartsWith("948-"))
            return "Hardeners";
        // Epoxy Activators (965- series)
        if (partNumber.StartsWith("965-"))
            return "Activators";

        return "Other";
    }

    public ColorTintItem? GetTint(string partNumber)
    {
        return _data.Tints.FirstOrDefault(t =>
            t.PartNumber?.Equals(partNumber, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public void UpdateTintPrice(string partNumber, decimal newPrice)
    {
        var tint = GetTint(partNumber);
        if (tint != null)
        {
            tint.Price = newPrice;
            SaveTints();
        }
    }

    public void AddCustomTint(string partNumber, string description, decimal price)
    {
        _data.Tints.Add(new ColorTintItem
        {
            PartNumber = partNumber,
            Description = description,
            Price = price
        });
        SaveTints();
    }

    #endregion

    #region Pricing Calculations

    public ColorTintSettings GetSettings() => _data.Settings;

    public void UpdateSettings(ColorTintSettings settings)
    {
        _data.Settings = settings;
        SaveSettings();
    }

    /// <summary>
    /// Get the cost price (base price from database)
    /// </summary>
    public decimal GetCostPrice(ColorTintItem tint) => tint.Price;

    /// <summary>
    /// Get shop price (your internal price, typically at cost or small markup)
    /// </summary>
    public decimal GetShopPrice(ColorTintItem tint)
    {
        return tint.Price * (1 + (_data.Settings.ShopMarkupPercent / 100m));
    }

    /// <summary>
    /// Get customer/insurance price (marked up price for billing)
    /// </summary>
    public decimal GetCustomerPrice(ColorTintItem tint)
    {
        return tint.Price * (1 + (_data.Settings.CustomerMarkupPercent / 100m));
    }

    /// <summary>
    /// Get price based on pricing tier
    /// </summary>
    public decimal GetPrice(ColorTintItem tint, PricingTier tier)
    {
        return tier switch
        {
            PricingTier.Cost => GetCostPrice(tint),
            PricingTier.Shop => GetShopPrice(tint),
            PricingTier.Customer => GetCustomerPrice(tint),
            _ => GetCustomerPrice(tint)
        };
    }

    #endregion

    #region Invoice Generation

    public ColorTintInvoice CreateInvoice(PricingTier tier)
    {
        return new ColorTintInvoice
        {
            InvoiceNumber = GenerateInvoiceNumber(),
            Date = DateTime.Now,
            PricingTier = tier,
            ShopName = _data.Settings.ShopName,
            ShopAddress = _data.Settings.ShopAddress,
            ShopCity = _data.Settings.ShopCity,
            ShopPhone = _data.Settings.ShopPhone,
            ShopFax = _data.Settings.ShopFax,
            TaxRate = _data.Settings.DefaultTaxRate,
            Items = new List<ColorTintInvoiceItem>()
        };
    }

    private string GenerateInvoiceNumber()
    {
        var date = DateTime.Now;
        var random = new Random().Next(1, 999);
        return $"{date:yyMMdd}-{random}";
    }

    public void AddItemToInvoice(ColorTintInvoice invoice, ColorTintItem tint, int quantity = 1)
    {
        var price = GetPrice(tint, invoice.PricingTier);
        invoice.Items.Add(new ColorTintInvoiceItem
        {
            PartNumber = tint.PartNumber ?? "",
            Description = tint.Description ?? "",
            Quantity = quantity,
            UnitPrice = price,
            LineTotal = price * quantity
        });
    }

    public decimal CalculateSubtotal(ColorTintInvoice invoice)
    {
        return invoice.Items.Sum(i => i.LineTotal);
    }

    public decimal CalculateTax(ColorTintInvoice invoice)
    {
        return CalculateSubtotal(invoice) * (invoice.TaxRate / 100m);
    }

    public decimal CalculateTotal(ColorTintInvoice invoice)
    {
        return CalculateSubtotal(invoice) + CalculateTax(invoice);
    }

    #endregion
}

#region Data Models

public class ColorTintData
{
    public List<ColorTintItem> Tints { get; set; } = new();
    public ColorTintSettings Settings { get; set; } = new();
}

public class ColorTintItem
{
    [JsonPropertyName("partNumber")]
    public string? PartNumber { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }
}

public class ColorTintSettings
{
    public decimal ShopMarkupPercent { get; set; } = 0;
    public decimal CustomerMarkupPercent { get; set; } = 15;
    public decimal DefaultTaxRate { get; set; } = 6.625m;
    public string ShopName { get; set; } = "";
    public string ShopAddress { get; set; } = "";
    public string ShopCity { get; set; } = "";
    public string ShopPhone { get; set; } = "";
    public string ShopFax { get; set; } = "";
}

public enum PricingTier
{
    Cost,      // Base cost price
    Shop,      // Your internal shop price
    Customer   // Customer/Insurance billing price
}

public class ColorTintInvoice
{
    public string InvoiceNumber { get; set; } = "";
    public DateTime Date { get; set; }
    public PricingTier PricingTier { get; set; }
    public string ShopName { get; set; } = "";
    public string ShopAddress { get; set; } = "";
    public string ShopCity { get; set; } = "";
    public string ShopPhone { get; set; } = "";
    public string ShopFax { get; set; } = "";
    public string BillTo { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string PaymentTerms { get; set; } = "Upon Receipt";
    public decimal TaxRate { get; set; }
    public List<ColorTintInvoiceItem> Items { get; set; } = new();
}

public class ColorTintInvoiceItem
{
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

#endregion
