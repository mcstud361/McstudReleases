#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

/// <summary>
/// Service for PPF (Paint Protection Film) pricing calculations.
/// Supports vehicle styles, individual panel selection, and volume discounts.
/// </summary>
public class PPFPricingService
{
    private static PPFPricingService? _instance;
    public static PPFPricingService Instance => _instance ??= new PPFPricingService();

    private PPFPricingData _data;
    private PPFUserSettings _userSettings;
    private readonly string _dataPath;
    private readonly string _settingsPath;

    public event EventHandler? DataChanged;

    private PPFPricingService()
    {
        _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PPFPricing.json");
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PPFUserSettings.json");
        _data = new PPFPricingData();
        _userSettings = new PPFUserSettings();
        LoadData();
        LoadUserSettings();
    }

    #region Data Loading

    private void LoadData()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = File.ReadAllText(_dataPath);
                var data = JsonSerializer.Deserialize<PPFPricingData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (data != null)
                {
                    _data = data;
                    System.Diagnostics.Debug.WriteLine($"[PPF] Loaded {_data.VehicleStyles?.Count ?? 0} vehicle styles, {_data.Panels?.Count ?? 0} panels");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PPF] Error loading data: {ex.Message}");
        }
    }

    private void LoadUserSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<PPFUserSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (settings != null)
                {
                    _userSettings = settings;
                }
            }
            else
            {
                // Create default settings
                _userSettings = new PPFUserSettings
                {
                    PriceMultiplier = 1.0m,
                    LaborRatePerHour = 75m,
                    DefaultProductId = "xpel_ultimate_plus",
                    ApplyVolumeDiscounts = true,
                    ShowCostBreakdown = false,
                    CustomPanelPrices = new Dictionary<string, Dictionary<string, decimal>>()
                };
                SaveUserSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PPF] Error loading user settings: {ex.Message}");
            _userSettings = new PPFUserSettings();
        }
    }

    public void SaveUserSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_userSettings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsPath, json);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PPF] Error saving user settings: {ex.Message}");
        }
    }

    #endregion

    #region Data Access

    public List<VehicleStyle> GetVehicleStyles() => _data.VehicleStyles ?? new List<VehicleStyle>();

    public VehicleStyle? GetVehicleStyle(string id) =>
        _data.VehicleStyles?.FirstOrDefault(v => v.Id == id);

    public List<PPFPanel> GetAllPanels() => _data.Panels ?? new List<PPFPanel>();

    public List<PPFPanel> GetPanelsByCategory(string category) =>
        _data.Panels?.Where(p => p.Category == category).ToList() ?? new List<PPFPanel>();

    public List<string> GetPanelCategories() =>
        _data.Panels?.Select(p => p.Category ?? "").Distinct().OrderBy(c => GetCategoryOrder(c)).ToList() ?? new List<string>();

    private int GetCategoryOrder(string category) => category switch
    {
        "front" => 0,
        "sides" => 1,
        "rear" => 2,
        "top" => 3,
        "glass" => 4,
        "interior" => 5,
        _ => 99
    };

    public List<PPFProduct> GetProducts() => _data.Products ?? new List<PPFProduct>();

    public PPFProduct? GetProduct(string id) =>
        _data.Products?.FirstOrDefault(p => p.Id == id);

    public List<PPFPackage> GetPackages() => _data.Packages ?? new List<PPFPackage>();

    public PPFPackage? GetPackage(string id) =>
        _data.Packages?.FirstOrDefault(p => p.Id == id);

    public PPFUserSettings GetUserSettings() => _userSettings;

    public void UpdateUserSettings(PPFUserSettings settings)
    {
        _userSettings = settings;
        SaveUserSettings();
    }

    #endregion

    #region Pricing Calculations

    /// <summary>
    /// Get the price for a specific panel on a specific vehicle type
    /// </summary>
    public decimal GetPanelPrice(string panelId, string vehicleStyleId, string? productId = null)
    {
        var vehicle = GetVehicleStyle(vehicleStyleId);
        if (vehicle == null) return 0;

        var sizeCategory = vehicle.SizeCategory ?? "medium";
        decimal basePrice;

        // Check for custom user price first
        if (_userSettings.CustomPanelPrices?.ContainsKey(panelId) == true &&
            _userSettings.CustomPanelPrices[panelId].ContainsKey(sizeCategory))
        {
            basePrice = _userSettings.CustomPanelPrices[panelId][sizeCategory];
        }
        // Check for panel-specific pricing
        else if (_data.DefaultPricing?.PanelPriceOverrides?.ContainsKey(panelId) == true)
        {
            var overrides = _data.DefaultPricing.PanelPriceOverrides[panelId];
            basePrice = overrides.ContainsKey(sizeCategory) ? overrides[sizeCategory] : GetDefaultPrice(sizeCategory);
        }
        else
        {
            basePrice = GetDefaultPrice(sizeCategory);
        }

        // Apply product multiplier
        var product = GetProduct(productId ?? _userSettings.DefaultProductId ?? "xpel_ultimate_plus");
        if (product != null)
        {
            basePrice *= (decimal)product.PriceMultiplier;
        }

        // Apply user's global price multiplier
        basePrice *= _userSettings.PriceMultiplier;

        return Math.Round(basePrice, 2);
    }

    private decimal GetDefaultPrice(string sizeCategory)
    {
        if (_data.DefaultPricing?.BasePricePerPanel?.ContainsKey(sizeCategory) == true)
        {
            return _data.DefaultPricing.BasePricePerPanel[sizeCategory];
        }
        return 175m; // Default fallback
    }

    /// <summary>
    /// Calculate total price for selected panels with optional volume discount
    /// </summary>
    public PPFQuote CalculateQuote(string vehicleStyleId, List<string> selectedPanelIds, string? productId = null)
    {
        var quote = new PPFQuote
        {
            VehicleStyleId = vehicleStyleId,
            VehicleStyle = GetVehicleStyle(vehicleStyleId),
            ProductId = productId ?? _userSettings.DefaultProductId ?? "xpel_ultimate_plus",
            Product = GetProduct(productId ?? _userSettings.DefaultProductId ?? "xpel_ultimate_plus"),
            Items = new List<PPFQuoteItem>(),
            CreatedDate = DateTime.Now
        };

        decimal subtotal = 0;

        foreach (var panelId in selectedPanelIds)
        {
            var panel = _data.Panels?.FirstOrDefault(p => p.Id == panelId);
            if (panel == null) continue;

            var price = GetPanelPrice(panelId, vehicleStyleId, quote.ProductId);

            quote.Items.Add(new PPFQuoteItem
            {
                PanelId = panelId,
                PanelName = panel.Name ?? "",
                PanelDescription = panel.Description ?? "",
                Category = panel.Category ?? "",
                Price = price
            });

            subtotal += price;
        }

        quote.Subtotal = subtotal;
        quote.PanelCount = quote.Items.Count;

        // Apply volume discount
        if (_userSettings.ApplyVolumeDiscounts && _data.DefaultPricing?.VolumeDiscounts != null)
        {
            var applicableDiscount = _data.DefaultPricing.VolumeDiscounts
                .Where(d => quote.PanelCount >= d.MinPanels)
                .OrderByDescending(d => d.MinPanels)
                .FirstOrDefault();

            if (applicableDiscount != null)
            {
                quote.DiscountPercent = applicableDiscount.Discount;
                quote.DiscountAmount = Math.Round(subtotal * (applicableDiscount.Discount / 100m), 2);
            }
        }

        quote.Total = quote.Subtotal - quote.DiscountAmount;

        return quote;
    }

    /// <summary>
    /// Calculate quote for a predefined package
    /// </summary>
    public PPFQuote CalculatePackageQuote(string vehicleStyleId, string packageId, string? productId = null)
    {
        var package = GetPackage(packageId);
        if (package == null)
        {
            return new PPFQuote { VehicleStyleId = vehicleStyleId };
        }

        var quote = CalculateQuote(vehicleStyleId, package.Panels ?? new List<string>(), productId);
        quote.PackageId = packageId;
        quote.PackageName = package.Name;

        return quote;
    }

    /// <summary>
    /// Set a custom price for a specific panel/size combination
    /// </summary>
    public void SetCustomPanelPrice(string panelId, string sizeCategory, decimal price)
    {
        if (_userSettings.CustomPanelPrices == null)
        {
            _userSettings.CustomPanelPrices = new Dictionary<string, Dictionary<string, decimal>>();
        }

        if (!_userSettings.CustomPanelPrices.ContainsKey(panelId))
        {
            _userSettings.CustomPanelPrices[panelId] = new Dictionary<string, decimal>();
        }

        _userSettings.CustomPanelPrices[panelId][sizeCategory] = price;
        SaveUserSettings();
    }

    /// <summary>
    /// Clear custom price for a panel (revert to default)
    /// </summary>
    public void ClearCustomPanelPrice(string panelId, string? sizeCategory = null)
    {
        if (_userSettings.CustomPanelPrices == null) return;

        if (sizeCategory == null)
        {
            _userSettings.CustomPanelPrices.Remove(panelId);
        }
        else if (_userSettings.CustomPanelPrices.ContainsKey(panelId))
        {
            _userSettings.CustomPanelPrices[panelId].Remove(sizeCategory);
        }

        SaveUserSettings();
    }

    /// <summary>
    /// Get price for a panel based on service type
    /// </summary>
    public decimal GetServicePanelPrice(string serviceType, string panelId, string sizeCategory)
    {
        // Check for custom service-specific price first
        if (_userSettings.ServicePanelPrices?.ContainsKey(serviceType) == true &&
            _userSettings.ServicePanelPrices[serviceType].ContainsKey(panelId) &&
            _userSettings.ServicePanelPrices[serviceType][panelId].ContainsKey(sizeCategory))
        {
            return _userSettings.ServicePanelPrices[serviceType][panelId][sizeCategory];
        }

        // Fall back to PPF pricing as base, adjusted by service type
        var basePrice = GetDefaultPrice(sizeCategory);

        // Check for panel-specific override in PPF data
        if (_data.DefaultPricing?.PanelPriceOverrides?.ContainsKey(panelId) == true)
        {
            var overrides = _data.DefaultPricing.PanelPriceOverrides[panelId];
            if (overrides.ContainsKey(sizeCategory))
                basePrice = overrides[sizeCategory];
        }

        // Apply service type multiplier (dynamic lookup, hardcoded fallback)
        var serviceConfig = GetServiceType(serviceType);
        var multiplier = serviceConfig?.PriceMultiplier ?? serviceType switch
        {
            "ceramic" => 0.6m,
            "ppf" => 1.0m,
            "vinyl" => 1.2m,
            _ => 1.0m
        };

        return Math.Round(basePrice * multiplier * _userSettings.PriceMultiplier, 2);
    }

    /// <summary>
    /// Set custom price for a panel in a specific service type
    /// </summary>
    public void SetServicePanelPrice(string serviceType, string panelId, string sizeCategory, decimal price)
    {
        if (_userSettings.ServicePanelPrices == null)
            _userSettings.ServicePanelPrices = new Dictionary<string, Dictionary<string, Dictionary<string, decimal>>>();

        if (!_userSettings.ServicePanelPrices.ContainsKey(serviceType))
            _userSettings.ServicePanelPrices[serviceType] = new Dictionary<string, Dictionary<string, decimal>>();

        if (!_userSettings.ServicePanelPrices[serviceType].ContainsKey(panelId))
            _userSettings.ServicePanelPrices[serviceType][panelId] = new Dictionary<string, decimal>();

        _userSettings.ServicePanelPrices[serviceType][panelId][sizeCategory] = price;
        SaveUserSettings();
    }

    /// <summary>
    /// Get custom products for a service type
    /// </summary>
    public List<CustomProduct> GetCustomProducts(string serviceType)
    {
        if (_userSettings.CustomProducts?.ContainsKey(serviceType) == true)
            return _userSettings.CustomProducts[serviceType];
        return new List<CustomProduct>();
    }

    /// <summary>
    /// Add or update a custom product for a service type
    /// </summary>
    public void SaveCustomProduct(string serviceType, CustomProduct product)
    {
        if (_userSettings.CustomProducts == null)
            _userSettings.CustomProducts = new Dictionary<string, List<CustomProduct>>();

        if (!_userSettings.CustomProducts.ContainsKey(serviceType))
            _userSettings.CustomProducts[serviceType] = new List<CustomProduct>();

        var existing = _userSettings.CustomProducts[serviceType].FirstOrDefault(p => p.Id == product.Id);
        if (existing != null)
        {
            existing.Name = product.Name;
            existing.Description = product.Description;
            existing.PriceMultiplier = product.PriceMultiplier;
        }
        else
        {
            _userSettings.CustomProducts[serviceType].Add(product);
        }

        SaveUserSettings();
    }

    /// <summary>
    /// Remove a custom product
    /// </summary>
    public void RemoveCustomProduct(string serviceType, string productId)
    {
        if (_userSettings.CustomProducts?.ContainsKey(serviceType) == true)
        {
            _userSettings.CustomProducts[serviceType].RemoveAll(p => p.Id == productId);
            SaveUserSettings();
        }
    }

    #endregion

    #region Service Type Customization

    private static readonly List<ServiceTypeConfig> BuiltInServiceTypes = new()
    {
        new ServiceTypeConfig { Id = "ppf", Name = "PPF", PriceMultiplier = 1.0m, IsBuiltIn = true, Order = 0 },
        new ServiceTypeConfig { Id = "vinyl", Name = "Vinyl Wrap", PriceMultiplier = 1.2m, IsBuiltIn = true, Order = 1 },
        new ServiceTypeConfig { Id = "ceramic", Name = "Ceramic Coat", PriceMultiplier = 0.6m, IsBuiltIn = true, Order = 2 }
    };

    public List<ServiceTypeConfig> GetServiceTypes()
    {
        var result = new List<ServiceTypeConfig>();

        foreach (var builtIn in BuiltInServiceTypes)
        {
            var userOverride = _userSettings.ServiceTypes?.FirstOrDefault(s => s.Id == builtIn.Id);
            if (userOverride != null)
            {
                if (!userOverride.IsHidden)
                    result.Add(userOverride);
            }
            else
            {
                result.Add(builtIn);
            }
        }

        if (_userSettings.ServiceTypes != null)
        {
            foreach (var custom in _userSettings.ServiceTypes.Where(s => !s.IsBuiltIn && !s.IsHidden))
            {
                if (!result.Any(r => r.Id == custom.Id))
                    result.Add(custom);
            }
        }

        return result.OrderBy(s => s.Order).ToList();
    }

    public ServiceTypeConfig? GetServiceType(string id)
    {
        var userOverride = _userSettings.ServiceTypes?.FirstOrDefault(s => s.Id == id);
        if (userOverride != null) return userOverride;
        return BuiltInServiceTypes.FirstOrDefault(s => s.Id == id);
    }

    public List<ServiceTypeConfig> GetAllServiceTypesIncludingHidden()
    {
        var result = new List<ServiceTypeConfig>();

        foreach (var builtIn in BuiltInServiceTypes)
        {
            var userOverride = _userSettings.ServiceTypes?.FirstOrDefault(s => s.Id == builtIn.Id);
            result.Add(userOverride ?? builtIn);
        }

        if (_userSettings.ServiceTypes != null)
        {
            foreach (var custom in _userSettings.ServiceTypes.Where(s => !s.IsBuiltIn))
            {
                if (!result.Any(r => r.Id == custom.Id))
                    result.Add(custom);
            }
        }

        return result.OrderBy(s => s.Order).ToList();
    }

    public void AddServiceType(string name, decimal multiplier)
    {
        _userSettings.ServiceTypes ??= new List<ServiceTypeConfig>();
        var maxOrder = _userSettings.ServiceTypes.Count > 0
            ? _userSettings.ServiceTypes.Max(s => s.Order)
            : BuiltInServiceTypes.Max(s => s.Order);

        _userSettings.ServiceTypes.Add(new ServiceTypeConfig
        {
            Id = $"custom_{Guid.NewGuid():N}".Substring(0, 20),
            Name = name,
            PriceMultiplier = multiplier,
            IsBuiltIn = false,
            IsHidden = false,
            Order = maxOrder + 1
        });
        SaveUserSettings();
    }

    public void UpdateServiceType(string id, string? name = null, decimal? multiplier = null)
    {
        EnsureServiceTypeInSettings(id);
        var st = _userSettings.ServiceTypes!.FirstOrDefault(s => s.Id == id);
        if (st == null) return;

        if (name != null) st.Name = name;
        if (multiplier.HasValue) st.PriceMultiplier = multiplier.Value;
        SaveUserSettings();
    }

    public void RemoveServiceType(string id)
    {
        EnsureServiceTypeInSettings(id);
        var st = _userSettings.ServiceTypes!.FirstOrDefault(s => s.Id == id);
        if (st == null) return;

        if (st.IsBuiltIn)
            st.IsHidden = true;
        else
            _userSettings.ServiceTypes!.Remove(st);

        SaveUserSettings();
    }

    public void RestoreServiceType(string id)
    {
        var st = _userSettings.ServiceTypes?.FirstOrDefault(s => s.Id == id);
        if (st != null)
        {
            st.IsHidden = false;
            SaveUserSettings();
        }
    }

    public void ReorderServiceType(string id, int direction)
    {
        var all = GetAllServiceTypesIncludingHidden();
        var idx = all.FindIndex(s => s.Id == id);
        if (idx < 0) return;

        var swapIdx = idx + direction;
        if (swapIdx < 0 || swapIdx >= all.Count) return;

        var tempOrder = all[idx].Order;
        EnsureServiceTypeInSettings(all[idx].Id);
        EnsureServiceTypeInSettings(all[swapIdx].Id);

        var a = _userSettings.ServiceTypes!.First(s => s.Id == all[idx].Id);
        var b = _userSettings.ServiceTypes!.First(s => s.Id == all[swapIdx].Id);
        a.Order = b.Order;
        b.Order = tempOrder;

        SaveUserSettings();
    }

    private void EnsureServiceTypeInSettings(string id)
    {
        _userSettings.ServiceTypes ??= new List<ServiceTypeConfig>();
        if (_userSettings.ServiceTypes.Any(s => s.Id == id)) return;

        var builtIn = BuiltInServiceTypes.FirstOrDefault(s => s.Id == id);
        if (builtIn != null)
        {
            _userSettings.ServiceTypes.Add(new ServiceTypeConfig
            {
                Id = builtIn.Id,
                Name = builtIn.Name,
                PriceMultiplier = builtIn.PriceMultiplier,
                IsBuiltIn = true,
                IsHidden = false,
                Order = builtIn.Order
            });
        }
    }

    public List<PPFPanel> GetVisiblePanels()
    {
        var all = GetAllPanels();
        if (_userSettings.HiddenPanels == null || _userSettings.HiddenPanels.Count == 0)
            return all;
        return all.Where(p => !_userSettings.HiddenPanels.Contains(p.Id ?? "")).ToList();
    }

    public List<string> GetHiddenPanelIds()
    {
        return _userSettings.HiddenPanels?.ToList() ?? new List<string>();
    }

    public void HidePanel(string panelId)
    {
        _userSettings.HiddenPanels ??= new HashSet<string>();
        _userSettings.HiddenPanels.Add(panelId);
        SaveUserSettings();
    }

    public void UnhidePanel(string panelId)
    {
        _userSettings.HiddenPanels?.Remove(panelId);
        SaveUserSettings();
    }

    public List<CustomPanelItem> GetCustomPanelItems(string serviceType)
    {
        if (_userSettings.CustomPanelItems?.ContainsKey(serviceType) == true)
            return _userSettings.CustomPanelItems[serviceType];
        return new List<CustomPanelItem>();
    }

    public void AddCustomPanelItem(string serviceType, CustomPanelItem item)
    {
        _userSettings.CustomPanelItems ??= new Dictionary<string, List<CustomPanelItem>>();
        if (!_userSettings.CustomPanelItems.ContainsKey(serviceType))
            _userSettings.CustomPanelItems[serviceType] = new List<CustomPanelItem>();

        _userSettings.CustomPanelItems[serviceType].Add(item);
        SaveUserSettings();
    }

    public void UpdateCustomPanelItem(string serviceType, CustomPanelItem item)
    {
        if (_userSettings.CustomPanelItems?.ContainsKey(serviceType) != true) return;

        var existing = _userSettings.CustomPanelItems[serviceType].FirstOrDefault(i => i.Id == item.Id);
        if (existing != null)
        {
            existing.Name = item.Name;
            existing.Description = item.Description;
            existing.Category = item.Category;
            existing.DefaultPrices = item.DefaultPrices;
            SaveUserSettings();
        }
    }

    public void RemoveCustomPanelItem(string serviceType, string itemId)
    {
        if (_userSettings.CustomPanelItems?.ContainsKey(serviceType) != true) return;
        _userSettings.CustomPanelItems[serviceType].RemoveAll(i => i.Id == itemId);
        SaveUserSettings();
    }

    #endregion
}

#region Data Models

public class PPFPricingData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("vehicleStyles")]
    public List<VehicleStyle>? VehicleStyles { get; set; }

    [JsonPropertyName("panels")]
    public List<PPFPanel>? Panels { get; set; }

    [JsonPropertyName("packages")]
    public List<PPFPackage>? Packages { get; set; }

    [JsonPropertyName("products")]
    public List<PPFProduct>? Products { get; set; }

    [JsonPropertyName("defaultPricing")]
    public DefaultPricing? DefaultPricing { get; set; }
}

public class VehicleStyle
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sizeCategory")]
    public string? SizeCategory { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public class PPFPanel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class PPFPackage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("panels")]
    public List<string>? Panels { get; set; }
}

public class PPFProduct
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("warranty")]
    public string? Warranty { get; set; }

    [JsonPropertyName("finish")]
    public string? Finish { get; set; }

    [JsonPropertyName("selfHealing")]
    public bool SelfHealing { get; set; }

    [JsonPropertyName("priceMultiplier")]
    public double PriceMultiplier { get; set; } = 1.0;
}

public class DefaultPricing
{
    [JsonPropertyName("basePricePerPanel")]
    public Dictionary<string, decimal>? BasePricePerPanel { get; set; }

    [JsonPropertyName("panelPriceOverrides")]
    public Dictionary<string, Dictionary<string, decimal>>? PanelPriceOverrides { get; set; }

    [JsonPropertyName("volumeDiscounts")]
    public List<VolumeDiscount>? VolumeDiscounts { get; set; }
}

public class VolumeDiscount
{
    [JsonPropertyName("minPanels")]
    public int MinPanels { get; set; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }
}

public class PPFUserSettings
{
    public decimal PriceMultiplier { get; set; } = 1.0m;
    public decimal LaborRatePerHour { get; set; } = 75m;
    public string? DefaultProductId { get; set; } = "xpel_ultimate_plus";
    public bool ApplyVolumeDiscounts { get; set; } = true;
    public bool ShowCostBreakdown { get; set; } = false;
    public Dictionary<string, Dictionary<string, decimal>>? CustomPanelPrices { get; set; }

    // Service-specific pricing: serviceType -> panelId -> sizeCategory -> price
    public Dictionary<string, Dictionary<string, Dictionary<string, decimal>>>? ServicePanelPrices { get; set; }

    // Custom products per service type: serviceType -> list of products
    public Dictionary<string, List<CustomProduct>>? CustomProducts { get; set; }

    // Customizable service types (overrides/extends the 3 built-ins)
    public List<ServiceTypeConfig>? ServiceTypes { get; set; }

    // Panels the user has hidden from the list
    public HashSet<string>? HiddenPanels { get; set; }

    // User-added panel items per service type
    public Dictionary<string, List<CustomPanelItem>>? CustomPanelItems { get; set; }
}

public class CustomProduct
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public double PriceMultiplier { get; set; } = 1.0;
}

public class PPFQuote
{
    public string? VehicleStyleId { get; set; }
    public VehicleStyle? VehicleStyle { get; set; }
    public string? ProductId { get; set; }
    public PPFProduct? Product { get; set; }
    public string? PackageId { get; set; }
    public string? PackageName { get; set; }
    public List<PPFQuoteItem> Items { get; set; } = new();
    public int PanelCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class PPFQuoteItem
{
    public string? PanelId { get; set; }
    public string? PanelName { get; set; }
    public string? PanelDescription { get; set; }
    public string? Category { get; set; }
    public decimal Price { get; set; }
}

public class ServiceTypeConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceMultiplier { get; set; } = 1.0m;
    public bool IsBuiltIn { get; set; }
    public bool IsHidden { get; set; }
    public int Order { get; set; }
}

public class CustomPanelItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, decimal>? DefaultPrices { get; set; }
}

#endregion
