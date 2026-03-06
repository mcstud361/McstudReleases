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
