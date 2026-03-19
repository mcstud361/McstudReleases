#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace McStudDesktop.Services;

#region Models

public class PriceCatalog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Supplier { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime ModifiedDate { get; set; } = DateTime.Now;
    public List<PriceCatalogItem> Items { get; set; } = new();
}

public class PriceCatalogItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? PartNumber { get; set; }
    public string? Description { get; set; }
    public decimal CostPrice { get; set; }
    public decimal ListPrice { get; set; }
    public string? Category { get; set; }
    public string? Unit { get; set; }
}

#endregion

/// <summary>
/// Singleton service for managing supplier price catalogs.
/// Persists catalogs as JSON in %LocalAppData%/McStudDesktop/PriceCatalogs/.
/// Supports import from Excel (.xlsx), CSV, and PDF.
/// </summary>
public class PriceCatalogService
{
    private static PriceCatalogService? _instance;
    public static PriceCatalogService Instance => _instance ??= new PriceCatalogService();

    private readonly string _catalogsPath;
    private List<PriceCatalog> _catalogs = new();

    public event EventHandler? CatalogsChanged;

    private PriceCatalogService()
    {
        _catalogsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McStudDesktop", "PriceCatalogs"
        );
        Directory.CreateDirectory(_catalogsPath);
        LoadAll();
        SeedBuiltInCatalogIfEmpty();
    }

    #region Persistence

    private void LoadAll()
    {
        _catalogs.Clear();
        if (!Directory.Exists(_catalogsPath)) return;

        foreach (var file in Directory.GetFiles(_catalogsPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var catalog = JsonSerializer.Deserialize<PriceCatalog>(json, JsonOpts());
                if (catalog != null)
                    _catalogs.Add(catalog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PriceCatalog] Error loading {file}: {ex.Message}");
            }
        }
    }

    private void SaveCatalog(PriceCatalog catalog)
    {
        catalog.ModifiedDate = DateTime.Now;
        var filePath = Path.Combine(_catalogsPath, $"{catalog.Id}.json");
        var json = JsonSerializer.Serialize(catalog, JsonOpts());
        File.WriteAllText(filePath, json);
    }

    private static JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region Public API

    public List<PriceCatalog> GetAllCatalogs() => _catalogs.ToList();

    public PriceCatalog? GetCatalog(string id) => _catalogs.FirstOrDefault(c => c.Id == id);

    public void AddCatalog(PriceCatalog catalog)
    {
        _catalogs.Add(catalog);
        SaveCatalog(catalog);
        CatalogsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCatalog(PriceCatalog catalog)
    {
        SaveCatalog(catalog);
        CatalogsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DeleteCatalog(string id)
    {
        var catalog = _catalogs.FirstOrDefault(c => c.Id == id);
        if (catalog == null) return false;

        _catalogs.Remove(catalog);
        var filePath = Path.Combine(_catalogsPath, $"{id}.json");
        if (File.Exists(filePath)) File.Delete(filePath);

        CatalogsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Search across all catalogs by keyword (part number, description, category).
    /// </summary>
    public List<(PriceCatalog Catalog, PriceCatalogItem Item)> Search(string keyword, string? catalogId = null)
    {
        var results = new List<(PriceCatalog, PriceCatalogItem)>();
        var kw = keyword.ToLowerInvariant();

        var searchCatalogs = catalogId != null
            ? _catalogs.Where(c => c.Id == catalogId)
            : _catalogs;

        foreach (var catalog in searchCatalogs)
        {
            foreach (var item in catalog.Items)
            {
                if ((item.PartNumber?.ToLowerInvariant().Contains(kw) == true) ||
                    (item.Description?.ToLowerInvariant().Contains(kw) == true) ||
                    (item.Category?.ToLowerInvariant().Contains(kw) == true))
                {
                    results.Add((catalog, item));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Get all distinct categories across all catalogs (or a specific one).
    /// </summary>
    public List<string> GetCategories(string? catalogId = null)
    {
        var catalogs = catalogId != null
            ? _catalogs.Where(c => c.Id == catalogId)
            : _catalogs;

        return catalogs
            .SelectMany(c => c.Items)
            .Where(i => !string.IsNullOrEmpty(i.Category))
            .Select(i => i.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Seeds a built-in "Glasurit/Standox Paint System" catalog on first run.
    /// Tries ColorTints.json first, falls back to hardcoded sample data from the actual price sheet.
    /// </summary>
    private void SeedBuiltInCatalogIfEmpty()
    {
        if (_catalogs.Count > 0) return;

        try
        {
            // Try loading full catalog from JSON first
            var tintDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ColorTints.json");
            if (File.Exists(tintDataPath))
            {
                var json = File.ReadAllText(tintDataPath);
                var tints = JsonSerializer.Deserialize<List<ColorTintSeedItem>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tints != null && tints.Count > 0)
                {
                    var catalog = new PriceCatalog
                    {
                        Id = "built-in-glasurit",
                        Name = "Glasurit/Standox Paint System",
                        Supplier = "BASF Refinish",
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now
                    };

                    foreach (var tint in tints)
                    {
                        catalog.Items.Add(new PriceCatalogItem
                        {
                            PartNumber = tint.PartNumber,
                            Description = tint.Description,
                            ListPrice = tint.Price,
                            CostPrice = Math.Round(tint.Price * 0.65m, 2),
                            Category = ColorTintService.GetCategory(tint.PartNumber)
                        });
                    }

                    AddCatalog(catalog);
                    System.Diagnostics.Debug.WriteLine($"[PriceCatalog] Seeded built-in catalog with {catalog.Items.Count} items from JSON");
                    return;
                }
            }

            // Fallback: seed from hardcoded data (curated from actual color tint price sheet)
            SeedHardcodedCatalog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PriceCatalog] Error seeding: {ex.Message}");
            try { SeedHardcodedCatalog(); } catch { }
        }
    }

    private void SeedHardcodedCatalog()
    {
        var catalog = new PriceCatalog
        {
            Id = "built-in-glasurit",
            Name = "Glasurit/Standox Paint System",
            Supplier = "BASF Refinish",
            CreatedDate = DateTime.Now,
            ModifiedDate = DateTime.Now
        };

        // Curated from actual BASF/Glasurit color tint price sheet
        var items = new (string Part, string Desc, decimal List)[]
        {
            // Standox Effect Tints (11-LE series)
            ("11-LE14U", "Mango Yellow", 827.98m),
            ("11-LE165", "Argent Shimmer", 827.98m),
            ("11-LE115", "Gold Sparkle", 929.00m),
            ("11-LE315", "Torch Shimmer", 827.98m),
            ("11-LE331", "Red Ember", 929.00m),
            ("11-LE333", "Metallic Red", 929.00m),
            ("11-LE350", "Hazel", 827.98m),
            ("11-LE385", "Magenta Shimmer", 827.98m),
            ("11-LE405", "Violet", 827.98m),
            ("11-LE505", "Deep Blue Pearl", 827.98m),
            ("11-LE525", "Blue Sparkle", 575.00m),
            ("11-LE535", "Violet Shimmer", 827.98m),
            ("11-LE615", "Jewel Shimmer", 827.98m),
            ("11-LE915", "Bronze Shimmer", 827.98m),
            ("11-LE990", "Oyster Pearl Effect", 827.98m),
            // Standox Pearls/Effects (11-E series)
            ("11-E014", "Fine White Pearl 2", 249.95m),
            ("11-E120", "Gold Glitter", 249.95m),
            ("11-E290", "Martian Dust", 249.95m),
            ("11-E330", "Flash Pearl Red", 249.95m),
            ("11-E440", "Violet Pearl", 249.95m),
            ("11-E520", "Blue Glitter", 249.95m),
            ("11-E557", "Metallic Blue", 249.95m),
            ("11-E620", "Green Glitter", 249.95m),
            ("11-E660", "Turquoise Pearl", 249.95m),
            ("11-E830", "Copper Pearl", 249.95m),
            // Glasurit Basecoat Tints (100-B series)
            ("100-B005", "White", 882.35m),
            ("100-B006", "Tint White", 441.30m),
            ("100-B130", "Sun Yellow", 441.30m),
            ("100-B165", "Pure Yellow", 441.30m),
            ("100-B210", "Orange", 441.30m),
            ("100-B330", "Cranbury Red", 441.30m),
            ("100-B336", "Chili Red", 441.30m),
            ("100-B380", "Ruby Red", 441.30m),
            ("100-B450", "Violet", 441.30m),
            ("100-B553", "Royal Blue", 441.30m),
            ("100-B562", "Azure Blue", 882.35m),
            ("100-B610", "Grass Green", 441.30m),
            ("100-B915", "Black", 882.35m),
            ("100-B955", "Jet Black", 441.30m),
            ("100-B994", "Factory Jet Black", 882.35m),
            // Glasurit Effect Pearls (100-E series)
            ("100-E007", "Brilliant White", 550.90m),
            ("100-E014", "White Pearl", 617.05m),
            ("100-E156", "Yellow Pearl", 617.05m),
            ("100-E334", "Red Pearl", 617.05m),
            ("100-E337", "Brilliant Red", 550.90m),
            ("100-E512", "Fine Blue Pearl", 617.05m),
            ("100-E516", "Blue Pearl", 617.05m),
            ("100-E614", "Green Pearl", 137.75m),
            ("100-E985", "Anthracite Pearl", 617.05m),
            // Glasurit Metallic/Mixing (100-M series)
            ("100-M002", "Chrome Silver", 550.90m),
            ("100-M10", "Metallic Mixing Clear", 381.50m),
            ("100-M20", "Mixing Clear", 381.50m),
            ("100-M975", "Fine Silver", 882.35m),
            ("100-M990", "Brilliant Silver Super Fine", 882.35m),
            ("100-M993", "Fine Brilliant Silver", 882.35m),
            ("100-M995", "Brilliant Silver", 441.30m),
            ("100-M972", "Extra Fine Silver", 441.30m),
            // Blending/Additives
            ("100-MB40", "Blending Clear Extreme", 138.50m),
            ("100-MB50", "Blending Clear", 83.50m),
            ("100-MB5", "Tri-Coat Additive", 80.15m),
            ("100-MPT", "Tintable Sealer", 329.85m),
            // Waterborne Basecoat (90 series)
            ("90-1250", "W/B Jet Black", 897.90m),
            ("90-3A0", "Cherry Red", 1335.30m),
            ("90-905", "Velvet Silver III", 911.75m),
            ("90-A031", "White", 968.65m),
            ("90-A035", "Snow White", 986.65m),
            ("90-A924", "Waterborne Deep Black", 964.45m),
            ("90-M99/01", "Extra Fine Aluminum", 964.45m),
            ("90-M99/02", "Fine Aluminum", 975.30m),
            ("90-M4", "Mixing Base", 481.75m),
            ("90-M5", "Blending Clear", 88.40m),
            // Solventborne (55 series)
            ("55-1250", "Deep Black", 1325.50m),
            ("55-A125", "White Tint", 1315.45m),
            ("55-A535", "Sapphire Blue", 1315.45m),
            ("55-M0", "Mixing Clear", 1300.70m),
            ("55-M25", "White", 1300.70m),
            ("55-B500", "Blending Clear", 215.55m),
            // Primers & Sealers (285 series)
            ("285-10", "Low VOC Black DTM Primer Filler", 647.60m),
            ("285-13", "DTM Urethane Primer", 612.15m),
            ("285-20", "Low VOC White DTM Primer Filler", 647.60m),
            ("285-29", "Low VOC Black Sealer", 612.15m),
            ("285-50", "HS Prime Filler", 617.30m),
            ("285-60", "Universal Primer Filler", 597.80m),
            ("285-270", "Primer Filler Pro Grey", 278.95m),
            ("283-155", "Etching Primer", 152.75m),
            // Epoxy Primers (801 series)
            ("801-72", "Chromatic-Free Epoxy Primer", 730.05m),
            ("801-73", "Low VOC Epoxy Primer", 733.45m),
            ("801-74", "Elite Epoxy Primer Black", 583.60m),
            ("801-76", "Elite Epoxy Primer White", 583.60m),
            // Clearcoats (923 series)
            ("923-109", "HS UV Klarlack Clear", 656.45m),
            ("923-210", "Low VOC Ultimate Clear", 640.95m),
            ("923-222", "Fast Repair Low VOC Clear", 684.60m),
            ("923-230", "Low VOC All Purpose Clear", 652.70m),
            ("923-255", "HS Multi-Clear", 649.15m),
            ("923-365", "Glamour Production Clear", 542.45m),
            ("923-450", "Multi-Temp Baking Clear", 483.20m),
            ("923-550", "High Performance Clear", 547.85m),
            // Hardeners (929 series)
            ("929-105", "Low VOC Extra Rapid Hardener", 272.95m),
            ("929-115", "Low VOC Rapid Hardener", 272.95m),
            ("929-120", "Low VOC Medium Hardener", 272.95m),
            ("929-130", "Low VOC Slow Hardener", 272.95m),
            ("929-245", "All Purpose Hardener", 660.25m),
            ("929-91", "HS Topcoat Hardener - Fast", 300.60m),
            ("929-93", "HS Topcoat Hardener - Normal", 300.60m),
            // Reducers (352 series)
            ("352-50", "Reducer - Fast", 225.60m),
            ("352-91", "Reducer - Normal", 246.70m),
            ("352-216", "Reducer - Slow", 246.40m),
            ("352-720", "LVOC Normal Reducer", 246.70m),
            ("352-500", "Spot Blender", 84.35m),
            // Additives (522 series)
            ("522-111", "Elastifier Additive (Soft Face)", 282.25m),
            ("522-300", "Gloss Reducer Paste", 582.20m),
            ("522-333", "Low VOC Elastifier Additive", 235.45m),
            ("522-M0", "Mixing Clear", 925.60m),
            // Adhesion Promoters (934 series)
            ("934-10", "1K Plastic Primer", 52.85m),
            ("934-30", "1K Plastic Adhesion Promoter", 255.55m),
            ("934-71", "Low VOC Black AdPro Sealer", 642.85m),
            // Body Fillers (839 series)
            ("839-20", "Double Plus Body Filler", 130.15m),
            ("839-90", "Plastics Body Filler", 146.70m),
            // Cleaners
            ("541-5", "Silicone and Tar Remover", 125.60m),
            ("541-30", "Universal Cleaner for Plastics", 104.55m),
            ("360-4", "Metal Cleaner", 172.15m),
            // Epoxy Activators (965 series)
            ("965-60", "Epoxy Primer Activator", 206.85m),
            ("965-61", "Low VOC Epoxy Activator", 254.10m),
            ("965-63", "Elite Epoxy Primer Activator", 185.90m),
        };

        foreach (var (part, desc, list) in items)
        {
            catalog.Items.Add(new PriceCatalogItem
            {
                PartNumber = part,
                Description = desc,
                ListPrice = list,
                CostPrice = Math.Round(list * 0.65m, 2),
                Category = ColorTintService.GetCategory(part)
            });
        }

        AddCatalog(catalog);
        System.Diagnostics.Debug.WriteLine($"[PriceCatalog] Seeded built-in catalog with {catalog.Items.Count} hardcoded items");
    }

    // Minimal model for reading ColorTints.json during seeding
    private class ColorTintSeedItem
    {
        [JsonPropertyName("partNumber")]
        public string? PartNumber { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("price")]
        public decimal Price { get; set; }
    }

    #endregion

    #region Import — Excel (.xlsx)

    /// <summary>
    /// Import items from an Excel file. Auto-detects columns by header names.
    /// </summary>
    public async Task<PriceCatalog> ImportFromExcelAsync(string filePath, string catalogName, string? supplier = null)
    {
        return await Task.Run(() =>
        {
            var catalog = new PriceCatalog { Name = catalogName, Supplier = supplier };

            using var workbook = new ClosedXML.Excel.XLWorkbook(filePath);
            var ws = workbook.Worksheets.First();

            // Auto-detect columns from header row
            var headerRow = ws.Row(1);
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in headerRow.CellsUsed())
            {
                var header = cell.GetString().Trim().ToLowerInvariant();
                var colNum = cell.Address.ColumnNumber;

                if (header.Contains("part") || header.Contains("sku") || header.Contains("item #") || header.Contains("item#"))
                    colMap.TryAdd("partNumber", colNum);
                else if (header.Contains("desc") || header.Contains("name") || header.Contains("product"))
                    colMap.TryAdd("description", colNum);
                else if (header.Contains("cost") || header.Contains("dealer") || header.Contains("wholesale"))
                    colMap.TryAdd("cost", colNum);
                else if (header.Contains("list") || header.Contains("retail") || header.Contains("msrp") || header.Contains("price"))
                    colMap.TryAdd("list", colNum);
                else if (header.Contains("categ") || header.Contains("group") || header.Contains("type"))
                    colMap.TryAdd("category", colNum);
                else if (header.Contains("unit") || header.Contains("uom") || header.Contains("measure"))
                    colMap.TryAdd("unit", colNum);
            }

            // If no "cost" found but "price" found, treat it as list
            // If only one price column, use it as both
            if (!colMap.ContainsKey("cost") && !colMap.ContainsKey("list"))
            {
                // Look for any numeric-looking column
                foreach (var cell in headerRow.CellsUsed())
                {
                    var h = cell.GetString().Trim().ToLowerInvariant();
                    if (h.Contains("price") || h.Contains("$") || h.Contains("amount"))
                    {
                        colMap["list"] = cell.Address.ColumnNumber;
                        break;
                    }
                }
            }

            // Read data rows
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                var desc = colMap.ContainsKey("description") ? row.Cell(colMap["description"]).GetString().Trim() : "";
                var partNum = colMap.ContainsKey("partNumber") ? row.Cell(colMap["partNumber"]).GetString().Trim() : "";

                // Skip empty rows
                if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(partNum))
                    continue;

                var item = new PriceCatalogItem
                {
                    PartNumber = string.IsNullOrWhiteSpace(partNum) ? null : partNum,
                    Description = string.IsNullOrWhiteSpace(desc) ? partNum : desc,
                    CostPrice = colMap.ContainsKey("cost") ? ParseDecimal(row.Cell(colMap["cost"]).GetString()) : 0,
                    ListPrice = colMap.ContainsKey("list") ? ParseDecimal(row.Cell(colMap["list"]).GetString()) : 0,
                    Category = colMap.ContainsKey("category") ? row.Cell(colMap["category"]).GetString().Trim() : null,
                    Unit = colMap.ContainsKey("unit") ? row.Cell(colMap["unit"]).GetString().Trim() : null
                };

                catalog.Items.Add(item);
            }

            return catalog;
        });
    }

    #endregion

    #region Import — CSV

    /// <summary>
    /// Import items from a CSV file. Auto-detects delimiter and maps columns by header.
    /// </summary>
    public async Task<PriceCatalog> ImportFromCsvAsync(string filePath, string catalogName, string? supplier = null)
    {
        return await Task.Run(() =>
        {
            var catalog = new PriceCatalog { Name = catalogName, Supplier = supplier };
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return catalog;

            // Detect delimiter
            var delimiter = DetectCsvDelimiter(lines[0]);

            // Parse header
            var headers = SplitCsvLine(lines[0], delimiter);
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].Trim().ToLowerInvariant();
                if (h.Contains("part") || h.Contains("sku") || h.Contains("item"))
                    colMap.TryAdd("partNumber", i);
                else if (h.Contains("desc") || h.Contains("name") || h.Contains("product"))
                    colMap.TryAdd("description", i);
                else if (h.Contains("cost") || h.Contains("dealer") || h.Contains("wholesale"))
                    colMap.TryAdd("cost", i);
                else if (h.Contains("list") || h.Contains("retail") || h.Contains("msrp") || h.Contains("price"))
                    colMap.TryAdd("list", i);
                else if (h.Contains("categ") || h.Contains("group") || h.Contains("type"))
                    colMap.TryAdd("category", i);
                else if (h.Contains("unit") || h.Contains("uom"))
                    colMap.TryAdd("unit", i);
            }

            // Read data rows
            for (int r = 1; r < lines.Length; r++)
            {
                var fields = SplitCsvLine(lines[r], delimiter);
                var desc = GetField(fields, colMap, "description");
                var partNum = GetField(fields, colMap, "partNumber");

                if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(partNum))
                    continue;

                catalog.Items.Add(new PriceCatalogItem
                {
                    PartNumber = string.IsNullOrWhiteSpace(partNum) ? null : partNum,
                    Description = string.IsNullOrWhiteSpace(desc) ? partNum : desc,
                    CostPrice = ParseDecimal(GetField(fields, colMap, "cost")),
                    ListPrice = ParseDecimal(GetField(fields, colMap, "list")),
                    Category = GetField(fields, colMap, "category"),
                    Unit = GetField(fields, colMap, "unit")
                });
            }

            return catalog;
        });
    }

    private static char DetectCsvDelimiter(string headerLine)
    {
        var candidates = new[] { ',', '\t', ';', '|' };
        return candidates.OrderByDescending(d => headerLine.Count(c => c == d)).First();
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        // Simple CSV split (handles quoted fields)
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    private static string GetField(string[] fields, Dictionary<string, int> colMap, string key)
    {
        if (colMap.TryGetValue(key, out var idx) && idx < fields.Length)
            return fields[idx].Trim();
        return "";
    }

    #endregion

    #region Import — PDF (heuristic)

    /// <summary>
    /// Import items from a PDF price sheet using text extraction and heuristic parsing.
    /// </summary>
    public async Task<PriceCatalog> ImportFromPdfAsync(string filePath, string catalogName, string? supplier = null)
    {
        return await Task.Run(() =>
        {
            var catalog = new PriceCatalog { Name = catalogName, Supplier = supplier };

            var text = PdfTextExtractorService.ExtractText(filePath);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Heuristic: find lines that contain a price pattern ($xx.xx or xx.xx)
            var priceRegex = new Regex(@"\$?\d+\.\d{2}");

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var matches = priceRegex.Matches(trimmed);
                if (matches.Count == 0) continue;

                // Try to extract part number (leading alphanumeric pattern)
                string? partNumber = null;
                var partMatch = Regex.Match(trimmed, @"^([A-Za-z0-9][\w\-\.]{2,20})\s");
                if (partMatch.Success)
                    partNumber = partMatch.Groups[1].Value;

                // Description = text before the first price
                var firstPriceIdx = matches[0].Index;
                var desc = trimmed.Substring(0, firstPriceIdx).Trim();
                if (!string.IsNullOrEmpty(partNumber) && desc.StartsWith(partNumber))
                    desc = desc.Substring(partNumber.Length).Trim();

                if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(partNumber))
                    continue;

                // Parse prices
                decimal cost = 0, list = 0;
                var prices = matches.Select(m => ParseDecimal(m.Value)).Where(p => p > 0).ToList();

                if (prices.Count >= 2)
                {
                    cost = prices[0];
                    list = prices[1];
                }
                else if (prices.Count == 1)
                {
                    list = prices[0];
                }

                catalog.Items.Add(new PriceCatalogItem
                {
                    PartNumber = partNumber,
                    Description = string.IsNullOrWhiteSpace(desc) ? partNumber ?? "Item" : desc,
                    CostPrice = cost,
                    ListPrice = list
                });
            }

            return catalog;
        });
    }

    #endregion

    #region Helpers

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        // Remove $ and other currency symbols
        var cleaned = Regex.Replace(value, @"[^\d.\-]", "");
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    #endregion
}
