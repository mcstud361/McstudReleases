#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McStudDesktop.Services;

public enum WidgetType
{
    Checklists,
    LaborRates,
    TowBill,
    Invoices,
    PPFPricing,
    PriceCatalogs,
    MyDocs,
    TemplateForm
}

public class WidgetEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "\uE8A5";
    public string Description { get; set; } = "";
    public WidgetType WidgetType { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public int Order { get; set; }
    public string? TemplateId { get; set; }
}

public class ShopDocsLayoutConfig
{
    public string Version { get; set; } = "1.0";
    public List<WidgetEntry> Widgets { get; set; } = new();
}

public class ShopDocsLayoutService
{
    private static ShopDocsLayoutService? _instance;
    public static ShopDocsLayoutService Instance => _instance ??= new ShopDocsLayoutService();

    private readonly string _configPath;
    private ShopDocsLayoutConfig _config;

    public event EventHandler? LayoutChanged;

    private ShopDocsLayoutService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop");
        Directory.CreateDirectory(appDataDir);
        _configPath = Path.Combine(appDataDir, "ShopDocsLayout.json");
        _config = LoadOrCreateConfig();
    }

    private ShopDocsLayoutConfig LoadOrCreateConfig()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ShopDocsLayoutConfig>(json, GetJsonOptions());
                if (config != null && config.Widgets.Count > 0)
                {
                    // Migrate old "Labor Rates" title to "Dealer Information"
                    var laborWidget = config.Widgets.FirstOrDefault(w => w.Id == "labor-rates");
                    if (laborWidget != null && laborWidget.Title == "Labor Rates")
                    {
                        laborWidget.Title = "Dealer Information";
                        laborWidget.Description = "Dealer contacts, labor rates & parts info";
                        try
                        {
                            var json2 = JsonSerializer.Serialize(config, GetJsonOptions());
                            File.WriteAllText(_configPath, json2);
                        }
                        catch { }
                    }

                    System.Diagnostics.Debug.WriteLine($"[ShopDocsLayout] Loaded {config.Widgets.Count} widgets from config");
                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShopDocsLayout] Error loading config: {ex.Message}");
            }
        }

        return CreateDefaultConfig();
    }

    private ShopDocsLayoutConfig CreateDefaultConfig()
    {
        var config = new ShopDocsLayoutConfig
        {
            Widgets = new List<WidgetEntry>
            {
                new WidgetEntry
                {
                    Id = "checklists",
                    Title = "Checklists",
                    Icon = "\uE8A5",
                    Description = "Vehicle inspection & QC checklists",
                    WidgetType = WidgetType.Checklists,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 0
                },
                new WidgetEntry
                {
                    Id = "labor-rates",
                    Title = "Dealer Information",
                    Icon = "\uE8D4",
                    Description = "Dealer contacts, labor rates & parts info",
                    WidgetType = WidgetType.LaborRates,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 1
                },
                new WidgetEntry
                {
                    Id = "tow-bill",
                    Title = "Tow Bill",
                    Icon = "\uE804",
                    Description = "Create & print tow bills",
                    WidgetType = WidgetType.TowBill,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 2
                },
                new WidgetEntry
                {
                    Id = "invoices",
                    Title = "Invoices",
                    Icon = "\uE9F9",
                    Description = "Color tint & shop stock invoices",
                    WidgetType = WidgetType.Invoices,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 3
                },
                new WidgetEntry
                {
                    Id = "ppf-pricing",
                    Title = "Vehicle Protection",
                    Icon = "\uE8B9",
                    Description = "PPF, ceramic coat & vinyl wrap quotes",
                    WidgetType = WidgetType.PPFPricing,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 4
                },
                new WidgetEntry
                {
                    Id = "price-catalogs",
                    Title = "Price Catalogs",
                    Icon = "\uE8B5",
                    Description = "Supplier price sheets for auto-fill",
                    WidgetType = WidgetType.PriceCatalogs,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 5
                },
                new WidgetEntry
                {
                    Id = "my-docs",
                    Title = "My Docs",
                    Icon = "\uE8B7",
                    Description = "Your custom documents folder",
                    WidgetType = WidgetType.MyDocs,
                    IsBuiltIn = true,
                    IsVisible = true,
                    Order = 6
                }
            }
        };

        return config;
    }

    public ShopDocsLayoutConfig GetLayout() => _config;

    public List<WidgetEntry> GetVisibleWidgets()
    {
        return _config.Widgets
            .Where(w => w.IsVisible)
            .OrderBy(w => w.Order)
            .ToList();
    }

    public List<WidgetEntry> GetAllWidgets()
    {
        return _config.Widgets
            .OrderBy(w => w.Order)
            .ToList();
    }

    public void SaveLayout()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, GetJsonOptions());
            File.WriteAllText(_configPath, json);
            System.Diagnostics.Debug.WriteLine("[ShopDocsLayout] Config saved");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShopDocsLayout] Error saving config: {ex.Message}");
        }
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDefaults()
    {
        try
        {
            if (File.Exists(_configPath))
                File.Delete(_configPath);
        }
        catch { }

        _config = CreateDefaultConfig();
        SaveLayout();
    }

    public void MoveWidget(string id, int direction)
    {
        var sorted = _config.Widgets.OrderBy(w => w.Order).ToList();
        var idx = sorted.FindIndex(w => w.Id == id);
        if (idx < 0) return;

        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= sorted.Count) return;

        // Swap orders
        var temp = sorted[idx].Order;
        sorted[idx].Order = sorted[newIdx].Order;
        sorted[newIdx].Order = temp;

        SaveLayout();
    }

    public void SetWidgetVisibility(string id, bool visible)
    {
        var widget = _config.Widgets.FirstOrDefault(w => w.Id == id);
        if (widget == null) return;

        widget.IsVisible = visible;
        SaveLayout();
    }

    public WidgetEntry AddUserWidget(string title, string icon, string? templateId = null)
    {
        var maxOrder = _config.Widgets.Any() ? _config.Widgets.Max(w => w.Order) : -1;

        var widget = new WidgetEntry
        {
            Id = $"user-{Guid.NewGuid():N}",
            Title = title,
            Icon = icon,
            Description = "Custom form",
            WidgetType = WidgetType.TemplateForm,
            IsBuiltIn = false,
            IsVisible = true,
            Order = maxOrder + 1,
            TemplateId = templateId
        };

        _config.Widgets.Add(widget);
        SaveLayout();
        return widget;
    }

    public bool RemoveUserWidget(string id)
    {
        var widget = _config.Widgets.FirstOrDefault(w => w.Id == id);
        if (widget == null || widget.IsBuiltIn) return false;

        _config.Widgets.Remove(widget);
        SaveLayout();
        return true;
    }

    public void UpdateWidgetTitle(string id, string newTitle)
    {
        var widget = _config.Widgets.FirstOrDefault(w => w.Id == id);
        if (widget == null || widget.IsBuiltIn) return;

        widget.Title = newTitle;
        SaveLayout();
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
}
