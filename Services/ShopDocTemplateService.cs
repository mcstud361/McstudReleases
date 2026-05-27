#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

/// <summary>
/// Unified template management for all Shop Docs.
///
/// Pattern:
/// - Original Templates: Read-only, stored in Data/ShopDocTemplates/
/// - My Templates: User copies that can be edited, stored in AppData
///
/// Users can:
/// - Fill out and export any template
/// - "Make a Copy" to create editable version
/// - Edit their copies (change wording, add/remove fields, etc.)
/// </summary>
public class ShopDocTemplateService
{
    private static ShopDocTemplateService? _instance;
    public static ShopDocTemplateService Instance => _instance ??= new ShopDocTemplateService();

    private readonly string _originalTemplatesPath;
    private readonly string _userTemplatesPath;

    // Cached templates by type
    private Dictionary<ShopDocType, List<ShopDocTemplate>> _originalTemplates = new();
    private Dictionary<ShopDocType, List<ShopDocTemplate>> _userTemplates = new();

    private ShopDocTemplateService()
    {
        _originalTemplatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ShopDocTemplates");
        _userTemplatesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop", "ShopDocTemplates"
        );

        Directory.CreateDirectory(_userTemplatesPath);
        LoadAllTemplates();
    }

    #region Template Loading

    private void LoadAllTemplates()
    {
        foreach (ShopDocType docType in Enum.GetValues<ShopDocType>())
        {
            _originalTemplates[docType] = new List<ShopDocTemplate>();
            _userTemplates[docType] = new List<ShopDocTemplate>();
        }

        // Load original templates from app data folder
        LoadOriginalTemplates();

        // Load user's custom templates
        LoadUserTemplates();

        // If no original templates exist, create defaults
        EnsureDefaultTemplates();
    }

    private void LoadOriginalTemplates()
    {
        if (!Directory.Exists(_originalTemplatesPath))
        {
            Directory.CreateDirectory(_originalTemplatesPath);
            return;
        }

        foreach (var file in Directory.GetFiles(_originalTemplatesPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var template = JsonSerializer.Deserialize<ShopDocTemplate>(json, GetJsonOptions());
                if (template != null)
                {
                    template.IsOriginal = true;
                    template.IsReadOnly = true;
                    _originalTemplates[template.DocType].Add(template);
                    System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Loaded original: {template.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Error loading {file}: {ex.Message}");
            }
        }
    }

    private void LoadUserTemplates()
    {
        if (!Directory.Exists(_userTemplatesPath))
            return;

        foreach (var file in Directory.GetFiles(_userTemplatesPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var template = JsonSerializer.Deserialize<ShopDocTemplate>(json, GetJsonOptions());
                if (template != null)
                {
                    template.IsOriginal = false;
                    template.IsReadOnly = false;
                    _userTemplates[template.DocType].Add(template);
                    System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Loaded user template: {template.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Error loading {file}: {ex.Message}");
            }
        }
    }

    private void EnsureDefaultTemplates()
    {
        // Create default templates for each type if none exist
        if (!_originalTemplates[ShopDocType.TowBill].Any())
        {
            CreateDefaultTowBillTemplate();
        }
        if (!_originalTemplates[ShopDocType.ColorTintInvoice].Any())
        {
            CreateDefaultColorTintTemplate();
        }
        if (!_originalTemplates[ShopDocType.ShopStockInvoice].Any())
        {
            CreateDefaultShopStockTemplate();
        }
        if (!_originalTemplates[ShopDocType.PPFPricing].Any())
        {
            CreateDefaultPPFTemplate();
        }
        if (!_originalTemplates[ShopDocType.LaborRates].Any())
        {
            CreateDefaultLaborRatesTemplate();
        }
        if (!_originalTemplates[ShopDocType.VehicleIntakeForm].Any())
        {
            CreateDefaultVehicleIntakeTemplate();
        }
    }

    #endregion

    #region Default Template Creation

    private void CreateDefaultTowBillTemplate()
    {
        // Pre-fill shop name from settings if available
        var shopName = ShopDocsSettingsService.Instance.GetSettings().ShopName;

        var template = new ShopDocTemplate
        {
            Id = "tow-bill-standard",
            DocType = ShopDocType.TowBill,
            Name = "Standard Tow Bill",
            Description = "Simple tow bill with vehicle, notes, and equipment charges",
            Version = "2.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "info",
                    Title = "Tow Bill Info",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "shopName", Label = "Shop Name", FieldType = FieldType.Text, DefaultValue = string.IsNullOrEmpty(shopName) ? "" : shopName },
                        new TemplateField { Id = "roNumber", Label = "RO #", FieldType = FieldType.Text },
                        new TemplateField { Id = "vehicleYMM", Label = "Vehicle", FieldType = FieldType.Text, Placeholder = "Year Make Model" },
                        new TemplateField { Id = "towDate", Label = "Date", FieldType = FieldType.Date, IsRequired = true, DefaultValue = "TODAY" }
                    }
                },
                new TemplateSection
                {
                    Id = "notes",
                    Title = "Notes",
                    Icon = "\uE70B",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "notes", Label = "Notes", FieldType = FieldType.MultilineText, Placeholder = "Tow from shop to dealer, tow to shop, etc." }
                    }
                },
                new TemplateSection
                {
                    Id = "charges",
                    Title = "Additional Equipment",
                    Icon = "\uE8C7",
                    IsChargeSection = true,
                    ChargeItems = new List<TemplateChargeItem>
                    {
                        new TemplateChargeItem { Id = "dollies", Name = "Dollies", DefaultAmount = 75.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "winch", Name = "Winch", DefaultAmount = 100.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "strapsChains", Name = "Straps / Chains", DefaultAmount = 25.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "flatbed", Name = "Flatbed", DefaultAmount = 200.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "goJack", Name = "Go-Jack", DefaultAmount = 50.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "afterHours", Name = "After Hours", DefaultAmount = 100.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "fuelSurcharge", Name = "Fuel Surcharge", DefaultAmount = 25.00m, IsEnabled = false },
                        new TemplateChargeItem { Id = "lockout", Name = "Lock-out Service", DefaultAmount = 75.00m, IsEnabled = false }
                    }
                }
            },
            Settings = new TemplateSettings
            {
                IncludeTax = true,
                TaxRate = 0
            }
        };

        _originalTemplates[ShopDocType.TowBill].Add(template);
        SaveOriginalTemplate(template);
    }

    private void CreateDefaultColorTintTemplate()
    {
        var template = new ShopDocTemplate
        {
            Id = "color-tint-standard",
            DocType = ShopDocType.ColorTintInvoice,
            Name = "Color Tint Invoice",
            Description = "Invoice for color matching and tinting services",
            Version = "1.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "header",
                    Title = "Invoice Details",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "invoiceNumber", Label = "Invoice #", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "date", Label = "Date", FieldType = FieldType.Date, IsRequired = true, DefaultValue = "TODAY" }
                    }
                },
                new TemplateSection
                {
                    Id = "vehicle",
                    Title = "Vehicle Information",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "vehicleYMM", Label = "Year / Make / Model", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "colorCode", Label = "Color Code", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "colorName", Label = "Color Name", FieldType = FieldType.Text },
                        new TemplateField { Id = "roNumber", Label = "RO Number", FieldType = FieldType.Text }
                    }
                },
                new TemplateSection
                {
                    Id = "charges",
                    Title = "Charges",
                    IsChargeSection = true,
                    AllowAddItems = true,
                    ChargeItems = new List<TemplateChargeItem>
                    {
                        new TemplateChargeItem { Id = "colorMatch", Name = "Color Match", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Each" },
                        new TemplateChargeItem { Id = "paintMaterial", Name = "Paint Material", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Pints" },
                        new TemplateChargeItem { Id = "clearCoat", Name = "Clear Coat", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Pints" },
                        new TemplateChargeItem { Id = "primerSealer", Name = "Primer / Sealer", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Pints" },
                        new TemplateChargeItem { Id = "reducerHardener", Name = "Reducer / Hardener", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Pints" },
                        new TemplateChargeItem { Id = "mixingLabor", Name = "Mixing Labor", DefaultAmount = 0, IsEnabled = true },
                        new TemplateChargeItem { Id = "sprayout", Name = "Spray Out Card", DefaultAmount = 0, IsEnabled = true }
                    }
                },
                new TemplateSection
                {
                    Id = "notes",
                    Title = "Notes",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "notes", Label = "Notes", FieldType = FieldType.MultilineText }
                    }
                }
            }
        };

        _originalTemplates[ShopDocType.ColorTintInvoice].Add(template);
        SaveOriginalTemplate(template);
    }

    private void CreateDefaultShopStockTemplate()
    {
        var template = new ShopDocTemplate
        {
            Id = "shop-stock-standard",
            DocType = ShopDocType.ShopStockInvoice,
            Name = "Shop Stock Invoice",
            Description = "Invoice for shop stock/supplies used on repair",
            Version = "1.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "header",
                    Title = "Invoice Details",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "invoiceNumber", Label = "Invoice #", FieldType = FieldType.Text },
                        new TemplateField { Id = "date", Label = "Date", FieldType = FieldType.Date, DefaultValue = "TODAY" },
                        new TemplateField { Id = "roNumber", Label = "RO Number", FieldType = FieldType.Text, IsRequired = true }
                    }
                },
                new TemplateSection
                {
                    Id = "vehicle",
                    Title = "Vehicle",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "vehicleYMM", Label = "Year / Make / Model", FieldType = FieldType.Text },
                        new TemplateField { Id = "insuranceCompany", Label = "Insurance Company", FieldType = FieldType.Text }
                    }
                },
                new TemplateSection
                {
                    Id = "items",
                    Title = "Stock Items Used",
                    IsChargeSection = true,
                    AllowAddItems = true,
                    ChargeItems = new List<TemplateChargeItem>
                    {
                        new TemplateChargeItem { Id = "sandpaper", Name = "Sandpaper (assorted)", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Sheets" },
                        new TemplateChargeItem { Id = "masking", Name = "Masking Tape", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Rolls" },
                        new TemplateChargeItem { Id = "maskingPaper", Name = "Masking Paper", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Ft" },
                        new TemplateChargeItem { Id = "tackCloth", Name = "Tack Cloth", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Each" },
                        new TemplateChargeItem { Id = "mixingCups", Name = "Mixing Cups", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Each" },
                        new TemplateChargeItem { Id = "strainers", Name = "Strainers", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Each" },
                        new TemplateChargeItem { Id = "cleaners", Name = "Cleaners/Solvents", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Oz" },
                        new TemplateChargeItem { Id = "gloves", Name = "Gloves", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Pair" },
                        new TemplateChargeItem { Id = "rags", Name = "Shop Rags", DefaultAmount = 0, IsEnabled = true, ShowQuantity = true, QuantityLabel = "Each" }
                    }
                },
                new TemplateSection
                {
                    Id = "notes",
                    Title = "Notes",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "notes", Label = "Notes", FieldType = FieldType.MultilineText }
                    }
                }
            }
        };

        _originalTemplates[ShopDocType.ShopStockInvoice].Add(template);
        SaveOriginalTemplate(template);
    }

    private void CreateDefaultPPFTemplate()
    {
        var template = new ShopDocTemplate
        {
            Id = "ppf-standard",
            DocType = ShopDocType.PPFPricing,
            Name = "PPF/Vehicle Protection Quote",
            Description = "Quote for paint protection film and ceramic coating services",
            Version = "1.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "header",
                    Title = "Quote Details",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "quoteNumber", Label = "Quote #", FieldType = FieldType.Text },
                        new TemplateField { Id = "date", Label = "Date", FieldType = FieldType.Date, DefaultValue = "TODAY" },
                        new TemplateField { Id = "validUntil", Label = "Valid Until", FieldType = FieldType.Date }
                    }
                },
                new TemplateSection
                {
                    Id = "customer",
                    Title = "Customer",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "customerName", Label = "Customer Name", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "customerPhone", Label = "Phone", FieldType = FieldType.Phone },
                        new TemplateField { Id = "customerEmail", Label = "Email", FieldType = FieldType.Email }
                    }
                },
                new TemplateSection
                {
                    Id = "vehicle",
                    Title = "Vehicle",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "vehicleYMM", Label = "Year / Make / Model", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "vehicleColor", Label = "Color", FieldType = FieldType.Text }
                    }
                },
                new TemplateSection
                {
                    Id = "ppfServices",
                    Title = "PPF Coverage Options",
                    IsChargeSection = true,
                    IsCheckboxSection = true,
                    ChargeItems = new List<TemplateChargeItem>
                    {
                        new TemplateChargeItem { Id = "fullHood", Name = "Full Hood", DefaultAmount = 800, IsEnabled = true },
                        new TemplateChargeItem { Id = "partialHood", Name = "Partial Hood (24\")", DefaultAmount = 450, IsEnabled = true },
                        new TemplateChargeItem { Id = "fullFenders", Name = "Full Fenders (pair)", DefaultAmount = 600, IsEnabled = true },
                        new TemplateChargeItem { Id = "mirrors", Name = "Mirrors (pair)", DefaultAmount = 150, IsEnabled = true },
                        new TemplateChargeItem { Id = "doorEdges", Name = "Door Edges (4)", DefaultAmount = 100, IsEnabled = true },
                        new TemplateChargeItem { Id = "doorCups", Name = "Door Cups (4)", DefaultAmount = 100, IsEnabled = true },
                        new TemplateChargeItem { Id = "bumper", Name = "Front Bumper", DefaultAmount = 700, IsEnabled = true },
                        new TemplateChargeItem { Id = "headlights", Name = "Headlights (pair)", DefaultAmount = 200, IsEnabled = true },
                        new TemplateChargeItem { Id = "rockerPanels", Name = "Rocker Panels (pair)", DefaultAmount = 400, IsEnabled = true },
                        new TemplateChargeItem { Id = "fullFrontEnd", Name = "Full Front End Package", DefaultAmount = 2200, IsEnabled = true },
                        new TemplateChargeItem { Id = "trackPack", Name = "Track Pack (high impact)", DefaultAmount = 3500, IsEnabled = true }
                    }
                },
                new TemplateSection
                {
                    Id = "coatingServices",
                    Title = "Ceramic Coating Options",
                    IsChargeSection = true,
                    IsCheckboxSection = true,
                    ChargeItems = new List<TemplateChargeItem>
                    {
                        new TemplateChargeItem { Id = "ceramic1yr", Name = "Ceramic Coating (1 year)", DefaultAmount = 500, IsEnabled = true },
                        new TemplateChargeItem { Id = "ceramic3yr", Name = "Ceramic Coating (3 year)", DefaultAmount = 900, IsEnabled = true },
                        new TemplateChargeItem { Id = "ceramic5yr", Name = "Ceramic Coating (5 year)", DefaultAmount = 1400, IsEnabled = true },
                        new TemplateChargeItem { Id = "wheelCoating", Name = "Wheel Coating", DefaultAmount = 200, IsEnabled = true },
                        new TemplateChargeItem { Id = "glassCoating", Name = "Glass Coating", DefaultAmount = 150, IsEnabled = true }
                    }
                },
                new TemplateSection
                {
                    Id = "notes",
                    Title = "Notes",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "notes", Label = "Notes", FieldType = FieldType.MultilineText }
                    }
                }
            }
        };

        _originalTemplates[ShopDocType.PPFPricing].Add(template);
        SaveOriginalTemplate(template);
    }

    private void CreateDefaultLaborRatesTemplate()
    {
        var template = new ShopDocTemplate
        {
            Id = "labor-rates-standard",
            DocType = ShopDocType.LaborRates,
            Name = "Labor Rates Calculator",
            Description = "Calculate labor costs based on hours and rates",
            Version = "1.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "rates",
                    Title = "Labor Rates",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "bodyRate", Label = "Body Labor Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "52.00" },
                        new TemplateField { Id = "paintRate", Label = "Paint Labor Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "52.00" },
                        new TemplateField { Id = "mechRate", Label = "Mechanical Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "75.00" },
                        new TemplateField { Id = "frameRate", Label = "Frame/Structural Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "60.00" },
                        new TemplateField { Id = "glassRate", Label = "Glass Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "50.00" },
                        new TemplateField { Id = "aluRate", Label = "Aluminum Rate ($/hr)", FieldType = FieldType.Currency, DefaultValue = "65.00" }
                    }
                },
                new TemplateSection
                {
                    Id = "hours",
                    Title = "Hours",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "bodyHours", Label = "Body Hours", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "paintHours", Label = "Paint Hours", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "mechHours", Label = "Mechanical Hours", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "frameHours", Label = "Frame Hours", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "glassHours", Label = "Glass Hours", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "aluHours", Label = "Aluminum Hours", FieldType = FieldType.Number, DefaultValue = "0" }
                    }
                }
            }
        };

        _originalTemplates[ShopDocType.LaborRates].Add(template);
        SaveOriginalTemplate(template);
    }

    private void CreateDefaultVehicleIntakeTemplate()
    {
        var template = new ShopDocTemplate
        {
            Id = "vehicle-intake-standard",
            DocType = ShopDocType.VehicleIntakeForm,
            Name = "Vehicle Check-In Report",
            Description = "Standard vehicle intake form with customer info, interior/exterior condition, and equipment checklist",
            Version = "1.0",
            IsOriginal = true,
            IsReadOnly = true,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "header",
                    Title = "Report Details",
                    Icon = "\uE8A5",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "date", Label = "Date", FieldType = FieldType.Date, IsRequired = true, DefaultValue = "TODAY" },
                        new TemplateField { Id = "writtenBy", Label = "Written By", FieldType = FieldType.Text },
                        new TemplateField { Id = "received", Label = "Received (Date/Time)", FieldType = FieldType.Text, Placeholder = "e.g. 3/26 10:30 AM" },
                        new TemplateField { Id = "promised", Label = "Promised (Date/Time)", FieldType = FieldType.Text, Placeholder = "e.g. 4/2 5:00 PM" }
                    }
                },
                new TemplateSection
                {
                    Id = "customer",
                    Title = "Customer Information",
                    Icon = "\uE77B",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "customerName", Label = "Name", FieldType = FieldType.Text, IsRequired = true },
                        new TemplateField { Id = "address", Label = "Address", FieldType = FieldType.Text },
                        new TemplateField { Id = "city", Label = "City / State / ZIP", FieldType = FieldType.Text },
                        new TemplateField { Id = "businessPhone", Label = "Business Phone", FieldType = FieldType.Phone },
                        new TemplateField { Id = "homePhone", Label = "Home Phone", FieldType = FieldType.Phone }
                    }
                },
                new TemplateSection
                {
                    Id = "vehicle",
                    Title = "Vehicle Information",
                    Icon = "\uE804",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "year", Label = "Year", FieldType = FieldType.Text },
                        new TemplateField { Id = "make", Label = "Make", FieldType = FieldType.Text },
                        new TemplateField { Id = "model", Label = "Model", FieldType = FieldType.Text },
                        new TemplateField { Id = "color", Label = "Color", FieldType = FieldType.Text },
                        new TemplateField { Id = "trim", Label = "Trim", FieldType = FieldType.Text },
                        new TemplateField { Id = "vin", Label = "VIN", FieldType = FieldType.Text, MaxLength = 17 },
                        new TemplateField { Id = "odometerIn", Label = "Odometer In", FieldType = FieldType.Number },
                        new TemplateField { Id = "odometerOut", Label = "Odometer Out", FieldType = FieldType.Number }
                    }
                },
                new TemplateSection
                {
                    Id = "insurance",
                    Title = "Insurance Information",
                    Icon = "\uE8C7",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "insuranceCompany", Label = "Insurance Company", FieldType = FieldType.Text },
                        new TemplateField { Id = "insurancePhone", Label = "Insurance Phone", FieldType = FieldType.Phone },
                        new TemplateField { Id = "claimNumber", Label = "Claim #", FieldType = FieldType.Text },
                        new TemplateField { Id = "adjuster", Label = "Adjuster", FieldType = FieldType.Text }
                    }
                },
                new TemplateSection
                {
                    Id = "interiorCondition",
                    Title = "Interior Condition & Equipment",
                    Icon = "\uE7C8",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "doorPanels", Label = "Door Panels", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "seats", Label = "Seats", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "centerConsole", Label = "Center Console", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "dashPanel", Label = "Dash Panel", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "headliner", Label = "Headliner", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "carpets", Label = "Carpets", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "floormats", Label = "Floormats", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "floormatCount", Label = "Floormat Count", FieldType = FieldType.Number, DefaultValue = "0" },
                        new TemplateField { Id = "tintedWindows", Label = "Tinted Windows", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "mirrors", Label = "Mirrors", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "interiorLights", Label = "Interior Lights", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "alarmSystem", Label = "Alarm System", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "alarmSystemCode", Label = "Alarm System Code", FieldType = FieldType.Text }
                    }
                },
                new TemplateSection
                {
                    Id = "miscEquipment",
                    Title = "Miscellaneous / Equipment",
                    Icon = "\uE74C",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "radioCode", Label = "Radio-Code", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "radioAntenna", Label = "Radio / Antenna", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "phoneAntenna", Label = "Phone / Antenna", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "cbAntenna", Label = "C.B. / Antenna", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "horn", Label = "Horn", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "sunroof", Label = "Sunroof", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "powerWindows", Label = "Power Windows", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "powerLocks", Label = "Power Locks", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "climateControl", Label = "Climate Control", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "exteriorLights", Label = "Exterior Lights", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "runningBoards", Label = "Running Boards", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "mudGuards", Label = "Mud Guards", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "wiperArmsBlades", Label = "Wiper Arms / Blades", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "spareTire", Label = "Spare Tire", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "jack", Label = "Jack", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "wheelCovers", Label = "Wheel Covers", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "wheelLock", Label = "Wheel Lock", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "pinstripe", Label = "Pinstripe Paint / Tape", FieldType = FieldType.Checkbox }
                    }
                },
                new TemplateSection
                {
                    Id = "paintCondition",
                    Title = "Paint Condition",
                    Icon = "\uE790",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "fallout", Label = "Fallout", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "fade", Label = "Fade", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "overspray", Label = "Overspray", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "checking", Label = "Checking", FieldType = FieldType.Checkbox },
                        new TemplateField { Id = "colorMatch", Label = "Color Match", FieldType = FieldType.Checkbox }
                    }
                },
                new TemplateSection
                {
                    Id = "exteriorNotes",
                    Title = "Exterior Condition Notes",
                    Icon = "\uE70B",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "exteriorNotes", Label = "Exterior condition notes, prior damage, dents, scratches, etc.", FieldType = FieldType.MultilineText }
                    }
                },
                new TemplateSection
                {
                    Id = "notes",
                    Title = "Additional Notes",
                    Icon = "\uE70B",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "notes", Label = "Additional Notes", FieldType = FieldType.MultilineText }
                    }
                },
                new TemplateSection
                {
                    Id = "signatures",
                    Title = "Signatures",
                    Icon = "\uE8D2",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "inspectedBy", Label = "Inspected By", FieldType = FieldType.Text },
                        new TemplateField { Id = "inspectedDate", Label = "Inspection Date", FieldType = FieldType.Date },
                        new TemplateField { Id = "customerSignature", Label = "Customer Signature", FieldType = FieldType.Text, HelpText = "Customer acknowledges this is a true representation of the vehicle's current condition" },
                        new TemplateField { Id = "customerSignDate", Label = "Customer Sign Date", FieldType = FieldType.Date }
                    }
                }
            }
        };

        _originalTemplates[ShopDocType.VehicleIntakeForm].Add(template);
        SaveOriginalTemplate(template);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Get all templates for a document type (originals + user's)
    /// </summary>
    public List<ShopDocTemplate> GetTemplates(ShopDocType docType)
    {
        var templates = new List<ShopDocTemplate>();
        templates.AddRange(_originalTemplates[docType]);
        templates.AddRange(_userTemplates[docType]);
        return templates;
    }

    /// <summary>
    /// Get only original (read-only) templates
    /// </summary>
    public List<ShopDocTemplate> GetOriginalTemplates(ShopDocType docType)
    {
        return _originalTemplates[docType].ToList();
    }

    /// <summary>
    /// Get only user's custom templates
    /// </summary>
    public List<ShopDocTemplate> GetUserTemplates(ShopDocType docType)
    {
        return _userTemplates[docType].ToList();
    }

    /// <summary>
    /// Get a specific template by ID
    /// </summary>
    public ShopDocTemplate? GetTemplate(ShopDocType docType, string templateId)
    {
        return _originalTemplates[docType].FirstOrDefault(t => t.Id == templateId)
            ?? _userTemplates[docType].FirstOrDefault(t => t.Id == templateId);
    }

    /// <summary>
    /// Make a copy of a template for the user to customize
    /// </summary>
    public ShopDocTemplate MakeCopy(ShopDocTemplate original, string newName)
    {
        var copy = original.DeepClone();
        copy.Id = $"user_{DateTime.Now.Ticks}";
        copy.Name = newName;
        copy.IsOriginal = false;
        copy.IsReadOnly = false;
        copy.CreatedDate = DateTime.Now;
        copy.ModifiedDate = DateTime.Now;
        copy.CopiedFromId = original.Id;

        _userTemplates[copy.DocType].Add(copy);
        SaveUserTemplate(copy);

        System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Created copy: {newName} from {original.Name}");
        return copy;
    }

    /// <summary>
    /// Save changes to a user template
    /// </summary>
    public void SaveUserTemplate(ShopDocTemplate template)
    {
        if (template.IsOriginal || template.IsReadOnly)
        {
            throw new InvalidOperationException("Cannot modify original templates. Make a copy first.");
        }

        template.ModifiedDate = DateTime.Now;

        var fileName = $"{template.Id}.json";
        var filePath = Path.Combine(_userTemplatesPath, fileName);
        var json = JsonSerializer.Serialize(template, GetJsonOptions());
        File.WriteAllText(filePath, json);

        System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Saved user template: {template.Name}");
    }

    /// <summary>
    /// Delete a user template
    /// </summary>
    public bool DeleteUserTemplate(ShopDocTemplate template)
    {
        if (template.IsOriginal)
            return false;

        _userTemplates[template.DocType].Remove(template);

        var fileName = $"{template.Id}.json";
        var filePath = Path.Combine(_userTemplatesPath, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Rename a user template
    /// </summary>
    public void RenameTemplate(ShopDocTemplate template, string newName)
    {
        if (template.IsOriginal || template.IsReadOnly)
            throw new InvalidOperationException("Cannot rename original templates.");

        template.Name = newName;
        SaveUserTemplate(template);
    }

    #endregion

    #region Custom Templates

    /// <summary>
    /// Create a blank template for user-created custom widgets
    /// </summary>
    public ShopDocTemplate CreateBlankTemplate(string name)
    {
        var template = new ShopDocTemplate
        {
            Id = $"custom_{DateTime.Now.Ticks}",
            DocType = ShopDocType.Custom,
            Name = name,
            Description = "Custom form",
            Version = "1.0",
            IsOriginal = false,
            IsReadOnly = false,
            CreatedDate = DateTime.Now,
            ModifiedDate = DateTime.Now,
            Sections = new List<TemplateSection>
            {
                new TemplateSection
                {
                    Id = "section1",
                    Title = "Section 1",
                    Fields = new List<TemplateField>
                    {
                        new TemplateField { Id = "field1", Label = "Field 1", FieldType = FieldType.Text }
                    }
                }
            }
        };

        _userTemplates[ShopDocType.Custom].Add(template);
        SaveUserTemplate(template);

        System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Created blank custom template: {name}");
        return template;
    }

    /// <summary>
    /// Make a copy of a template as a Custom type for widget use
    /// </summary>
    public ShopDocTemplate MakeCopyAsCustom(ShopDocTemplate original, string newName)
    {
        var copy = original.DeepClone();
        copy.Id = $"custom_{DateTime.Now.Ticks}";
        copy.Name = newName;
        copy.DocType = ShopDocType.Custom;
        copy.IsOriginal = false;
        copy.IsReadOnly = false;
        copy.CreatedDate = DateTime.Now;
        copy.ModifiedDate = DateTime.Now;
        copy.CopiedFromId = original.Id;

        _userTemplates[ShopDocType.Custom].Add(copy);
        SaveUserTemplate(copy);

        System.Diagnostics.Debug.WriteLine($"[ShopDocTemplate] Created custom copy: {newName} from {original.Name}");
        return copy;
    }

    #endregion

    #region Helpers

    private void SaveOriginalTemplate(ShopDocTemplate template)
    {
        Directory.CreateDirectory(_originalTemplatesPath);
        var fileName = $"{template.Id}.json";
        var filePath = Path.Combine(_originalTemplatesPath, fileName);
        var json = JsonSerializer.Serialize(template, GetJsonOptions());
        File.WriteAllText(filePath, json);
    }

    private JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    #endregion
}

#region Data Models

public enum ShopDocType
{
    TowBill,
    ColorTintInvoice,
    ShopStockInvoice,
    PPFPricing,
    LaborRates,
    VehicleIntakeForm,
    Custom
}

public enum SectionLayoutHint
{
    Auto,
    FullWidth,
    HalfWidth
}

public enum FieldType
{
    Text,
    MultilineText,
    Number,
    Currency,
    Date,
    Phone,
    Email,
    Checkbox,
    Dropdown
}

public class ShopDocTemplate
{
    public string Id { get; set; } = "";
    public ShopDocType DocType { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public bool IsOriginal { get; set; }
    public bool IsReadOnly { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime ModifiedDate { get; set; } = DateTime.Now;
    public string? CopiedFromId { get; set; }

    public List<TemplateSection> Sections { get; set; } = new();
    public TemplateSettings Settings { get; set; } = new();

    public ShopDocTemplate DeepClone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<ShopDocTemplate>(json)!;
    }
}

public class TemplateSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool IsCollapsible { get; set; }
    public bool IsChargeSection { get; set; }
    public bool IsCheckboxSection { get; set; } // Items are selectable checkboxes
    public bool AllowAddItems { get; set; } // User can add custom items
    public SectionLayoutHint LayoutHint { get; set; } = SectionLayoutHint.Auto;

    public List<TemplateField> Fields { get; set; } = new();
    public List<TemplateChargeItem> ChargeItems { get; set; } = new();
}

public class TemplateField
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public FieldType FieldType { get; set; } = FieldType.Text;
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public int? MaxLength { get; set; }
    public List<string>? DropdownOptions { get; set; }
    public string? HelpText { get; set; }
    public int Order { get; set; }
}

public class TemplateChargeItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal DefaultAmount { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsPerMile { get; set; }
    public bool IsPerDay { get; set; }
    public bool ShowQuantity { get; set; }
    public string? QuantityLabel { get; set; }
    public int Order { get; set; }
    public decimal DefaultCostPrice { get; set; }
    public decimal DefaultListPrice { get; set; }
    public string? PartNumber { get; set; }
    public string? CatalogItemId { get; set; }
}

public class TemplateSettings
{
    public bool IncludeTax { get; set; }
    public decimal TaxRate { get; set; }
    public string? PaymentTerms { get; set; }
    public string? ShopName { get; set; }
    public string? ShopAddress { get; set; }
    public string? ShopPhone { get; set; }
    public string? ShopEmail { get; set; }
    public string? LogoPath { get; set; }
}

#endregion
