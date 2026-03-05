#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using McstudDesktop.Models;

namespace McStudDesktop.Services;

/// <summary>
/// Centralizes estimate persistence and mining across all input paths
/// (PDF SMART, text paste, CSV/TXT upload, screen OCR, fallback parse).
/// </summary>
public static class EstimatePersistenceHelper
{
    /// <summary>
    /// Convert a ScreenOcrResult into a ParsedEstimate for storage.
    /// </summary>
    public static ParsedEstimate ConvertFromOcr(ScreenOcrResult ocrResult)
    {
        var estimate = new ParsedEstimate
        {
            Source = ocrResult.EstimateSource switch
            {
                OcrEstimateSource.CCCOne => "CCC",
                OcrEstimateSource.Mitchell => "Mitchell",
                OcrEstimateSource.Audatex => "Audatex",
                _ => "Unknown"
            },
            SourceFile = $"ScreenOCR_{ocrResult.Timestamp:yyyyMMdd_HHmmss}",
            RawText = ocrResult.RawText,
            ParsedDate = DateTime.Now
        };

        foreach (var op in ocrResult.DetectedOperations)
        {
            estimate.LineItems.Add(new PdfEstimateLineItem
            {
                Description = op.Description,
                PartName = op.PartName,
                OperationType = op.OperationType,
                LaborHours = op.LaborHours,
                RefinishHours = op.RefinishHours,
                Price = op.Price,
                Quantity = op.Quantity,
                RawLine = op.RawLine
            });
        }

        // Calculate totals from line items
        estimate.Totals.LaborTotal = estimate.LineItems.Sum(i => i.LaborHours);
        estimate.Totals.RefinishTotal = estimate.LineItems.Sum(i => i.RefinishHours);
        estimate.Totals.PartsTotal = estimate.LineItems.Where(i => i.Price > 0 && string.IsNullOrEmpty(i.OperationType)).Sum(i => i.Price);
        estimate.Totals.GrandTotal = estimate.LineItems.Sum(i => i.Price);

        return estimate;
    }

    /// <summary>
    /// Convert ParsedEstimateLines (from manual/fallback parsing) into a ParsedEstimate for storage.
    /// </summary>
    public static ParsedEstimate ConvertFromParsedLines(List<ParsedEstimateLine> lines, string rawText, string sourceFile)
    {
        var estimate = new ParsedEstimate
        {
            SourceFile = sourceFile,
            RawText = rawText,
            ParsedDate = DateTime.Now
        };

        // Extract metadata (Source/VehicleInfo/VIN) from the raw text via SMART parser
        try
        {
            var smartParsed = EstimatePdfParser.Instance.ParseText(rawText);
            if (!string.IsNullOrEmpty(smartParsed.Source) && smartParsed.Source != "Unknown")
                estimate.Source = smartParsed.Source;
            if (!string.IsNullOrEmpty(smartParsed.VehicleInfo))
                estimate.VehicleInfo = smartParsed.VehicleInfo;
            if (!string.IsNullOrEmpty(smartParsed.VIN))
                estimate.VIN = smartParsed.VIN;
        }
        catch
        {
            // Metadata extraction is best-effort
        }

        foreach (var line in lines)
        {
            estimate.LineItems.Add(new PdfEstimateLineItem
            {
                Description = line.Description,
                PartName = line.PartName,
                OperationType = line.OperationType,
                Section = line.Category,
                LaborHours = line.LaborHours,
                RefinishHours = line.RefinishHours,
                Price = line.Price,
                Quantity = line.Quantity,
                LaborType = line.LaborType,
                IsManualMarker = line.IsManualLine,
                ParentPartName = line.ParentPartName,
                RawLine = line.RawLine
            });
        }

        // Calculate totals from line items
        estimate.Totals.LaborTotal = estimate.LineItems.Sum(i => i.LaborHours);
        estimate.Totals.RefinishTotal = estimate.LineItems.Sum(i => i.RefinishHours);
        estimate.Totals.PartsTotal = estimate.LineItems.Where(i => i.Price > 0 && string.IsNullOrEmpty(i.OperationType)).Sum(i => i.Price);
        estimate.Totals.GrandTotal = estimate.LineItems.Sum(i => i.Price);

        return estimate;
    }

    /// <summary>
    /// Save a ParsedEstimate to the history database and mine it for patterns.
    /// This is the single entry point all code paths should use.
    /// </summary>
    public static void PersistAndMine(ParsedEstimate estimate)
    {
        try
        {
            var historyDb = EstimateHistoryDatabase.Instance;
            var estimateId = historyDb.AddEstimate(estimate);
            System.Diagnostics.Debug.WriteLine($"[EstimatePersistence] Saved estimate {estimateId} to history ({estimate.SourceFile})");

            try
            {
                var storedEstimate = historyDb.GetEstimateById(estimateId);
                if (storedEstimate != null)
                {
                    EstimateMiningEngine.Instance.LearnFromEstimate(storedEstimate);
                    System.Diagnostics.Debug.WriteLine($"[EstimatePersistence] Mined patterns from estimate {estimateId}");
                }
            }
            catch (Exception miningEx)
            {
                System.Diagnostics.Debug.WriteLine($"[EstimatePersistence] Mining failed: {miningEx.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EstimatePersistence] Failed to save: {ex.Message}");
        }
    }
}
