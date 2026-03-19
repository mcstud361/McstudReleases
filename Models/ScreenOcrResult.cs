#nullable enable
using System;
using System.Collections.Generic;

namespace McstudDesktop.Models;

public enum OcrEstimateSource
{
    Unknown,
    CCCOne,
    Mitchell,
    Audatex
}

public class OcrTextLine
{
    public string Text { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public double Confidence { get; set; }
}

public class OcrDetectedOperation
{
    public string Description { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public decimal LaborHours { get; set; }
    public decimal RefinishHours { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public string RawLine { get; set; } = string.Empty;
}

public class ScreenOcrResult
{
    public string RawText { get; set; } = string.Empty;
    public List<OcrTextLine> Lines { get; set; } = new();
    public List<OcrDetectedOperation> DetectedOperations { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourceWindow { get; set; } = string.Empty;
    public OcrEstimateSource EstimateSource { get; set; } = OcrEstimateSource.Unknown;
    public bool HasChanges { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetectedVin { get; set; }

    public int OperationCount => DetectedOperations.Count;
    public int LineCount => Lines.Count;
}
