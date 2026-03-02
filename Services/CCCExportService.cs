using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;

// Alias for FlaUI Window
using FlaWindow = FlaUI.Core.AutomationElements.Window;

namespace McStudDesktop.Services;

/// <summary>
/// Service for exporting operations directly into CCC Desktop using UI Automation
/// Uses human-like input to avoid detection and timing issues
/// </summary>
public class CCCExportService : IDisposable
{
    private readonly UIAutomationService _uiaService;
    private bool _disposed;

    // CCC Desktop field mapping - these will be populated by inspection
    // Format: field name -> (search method, identifier)
    private readonly Dictionary<string, FieldMapping> _fieldMappings = new();

    // Common CCC Desktop field names based on typical layout
    public static class Fields
    {
        public const string OperationType = "OperationType";
        public const string Description = "Description";
        public const string PartNumber = "PartNumber";
        public const string Quantity = "Quantity";
        public const string PartPrice = "PartPrice";
        public const string LaborHours = "LaborHours";
        public const string LaborType = "LaborType";
        public const string RefinishHours = "RefinishHours";
        public const string AddLine = "AddLine";
        public const string SaveLine = "SaveLine";
    }

    /// <summary>
    /// Typing speed: slow, normal, fast
    /// </summary>
    public enum TypingSpeed
    {
        Slow,   // 30-60ms
        Normal, // 15-45ms
        Fast    // 5-15ms
    }

    public CCCExportService()
    {
        _uiaService = new UIAutomationService();
        SetTypingSpeed(TypingSpeed.Normal);
    }

    /// <summary>
    /// Set the typing speed for human-like input
    /// </summary>
    public void SetTypingSpeed(TypingSpeed speed)
    {
        switch (speed)
        {
            case TypingSpeed.Slow:
                _uiaService.MinTypeDelay = 30;
                _uiaService.MaxTypeDelay = 60;
                break;
            case TypingSpeed.Normal:
                _uiaService.MinTypeDelay = 15;
                _uiaService.MaxTypeDelay = 45;
                break;
            case TypingSpeed.Fast:
                _uiaService.MinTypeDelay = 5;
                _uiaService.MaxTypeDelay = 15;
                break;
        }
    }

    /// <summary>
    /// Register a field mapping for CCC Desktop
    /// Call this after inspecting CCC to map discovered fields
    /// </summary>
    public void RegisterField(string fieldName, SearchMethod method, string identifier)
    {
        _fieldMappings[fieldName] = new FieldMapping(method, identifier);
    }

    /// <summary>
    /// Finds the CCC Desktop window
    /// </summary>
    public FlaWindow? FindCCCWindow()
    {
        // Try different possible window titles
        var window = _uiaService.FindWindowByTitle("CCC ONE");
        window ??= _uiaService.FindWindowByTitle("CCC Desktop");
        window ??= _uiaService.FindWindowByTitle("CCC");
        return window;
    }

    /// <summary>
    /// Check if CCC Desktop is running and accessible
    /// </summary>
    public bool IsCCCAvailable()
    {
        return FindCCCWindow() != null;
    }

    /// <summary>
    /// Get CCC window info for debugging
    /// </summary>
    public string GetCCCInfo()
    {
        var window = FindCCCWindow();
        if (window == null)
            return "CCC Desktop not found";

        return $"Found: {window.Name}\nAutomationId: {window.Properties.AutomationId.ValueOrDefault}\nClass: {window.Properties.ClassName.ValueOrDefault}";
    }

    /// <summary>
    /// Export a single operation row to CCC Desktop
    /// </summary>
    public async Task<CCCExportResult> ExportOperationAsync(
        OperationExportData operation,
        CancellationToken cancellationToken = default)
    {
        var result = new CCCExportResult { Success = false };

        try
        {
            var window = FindCCCWindow();
            if (window == null)
            {
                result.ErrorMessage = "CCC Desktop not found. Please make sure it's running.";
                return result;
            }

            // Focus the window
            window.Focus();
            await Task.Delay(100, cancellationToken);

            // Fill in each mapped field
            if (!string.IsNullOrEmpty(operation.OperationType))
            {
                await SetFieldAsync(window, Fields.OperationType, operation.OperationType, cancellationToken);
            }

            if (!string.IsNullOrEmpty(operation.Description))
            {
                await SetFieldAsync(window, Fields.Description, operation.Description, cancellationToken);
            }

            if (!string.IsNullOrEmpty(operation.PartNumber))
            {
                await SetFieldAsync(window, Fields.PartNumber, operation.PartNumber, cancellationToken);
            }

            if (operation.Quantity > 0)
            {
                await SetFieldAsync(window, Fields.Quantity, operation.Quantity.ToString(), cancellationToken);
            }

            if (operation.PartPrice > 0)
            {
                await SetFieldAsync(window, Fields.PartPrice, operation.PartPrice.ToString("F2"), cancellationToken);
            }

            if (operation.LaborHours > 0)
            {
                await SetFieldAsync(window, Fields.LaborHours, operation.LaborHours.ToString("F1"), cancellationToken);
            }

            if (operation.RefinishHours > 0)
            {
                await SetFieldAsync(window, Fields.RefinishHours, operation.RefinishHours.ToString("F1"), cancellationToken);
            }

            result.Success = true;
            result.FieldsSet = _fieldMappings.Count;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Export cancelled";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Export error: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Export multiple operations to CCC Desktop
    /// </summary>
    public async Task<CCCBatchExportResult> ExportOperationsAsync(
        List<OperationExportData> operations,
        IProgress<CCCExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CCCBatchExportResult();

        var window = FindCCCWindow();
        if (window == null)
        {
            result.ErrorMessage = "CCC Desktop not found";
            return result;
        }

        for (int i = 0; i < operations.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.WasCancelled = true;
                break;
            }

            progress?.Report(new CCCExportProgress
            {
                CurrentItem = i + 1,
                TotalItems = operations.Count,
                CurrentDescription = operations[i].Description,
                PercentComplete = (int)((i + 1) * 100.0 / operations.Count)
            });

            var opResult = await ExportOperationAsync(operations[i], cancellationToken);

            if (opResult.Success)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailCount++;
                result.Errors.Add($"Line {i + 1}: {opResult.ErrorMessage}");
            }

            // Small delay between operations
            await Task.Delay(100, cancellationToken);
        }

        result.TotalCount = operations.Count;
        return result;
    }

    /// <summary>
    /// Set a field value in CCC using the mapped method
    /// </summary>
    private async Task<bool> SetFieldAsync(FlaWindow window, string fieldName, string value, CancellationToken cancellationToken)
    {
        if (!_fieldMappings.TryGetValue(fieldName, out var mapping))
        {
            // No mapping found - skip silently
            return false;
        }

        return mapping.Method switch
        {
            SearchMethod.ByAutomationId => await _uiaService.ClickAndTypeByAutomationIdAsync(window, mapping.Identifier, value, cancellationToken),
            SearchMethod.ByName => await _uiaService.ClickAndTypeByNameAsync(window, mapping.Identifier, value, cancellationToken),
            SearchMethod.ByClassName => await _uiaService.ClickAndTypeByClassNameAsync(window, mapping.Identifier, value, cancellationToken),
            _ => false
        };
    }

    /// <summary>
    /// Inspect CCC Desktop and return field information
    /// </summary>
    public string InspectCCC()
    {
        return _uiaService.InspectCCCWindow();
    }

    /// <summary>
    /// Get all discovered text fields in CCC
    /// </summary>
    public List<ControlInfo> GetCCCTextFields()
    {
        var window = FindCCCWindow();
        if (window == null) return new List<ControlInfo>();

        return _uiaService.FindAllTextFields(window);
    }

    /// <summary>
    /// Get all discovered buttons in CCC
    /// </summary>
    public List<ControlInfo> GetCCCButtons()
    {
        var window = FindCCCWindow();
        if (window == null) return new List<ControlInfo>();

        return _uiaService.FindAllButtons(window);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _uiaService.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// How to search for a UI element
/// </summary>
public enum SearchMethod
{
    ByAutomationId,
    ByName,
    ByClassName
}

/// <summary>
/// Mapping information for a CCC field
/// </summary>
public record FieldMapping(SearchMethod Method, string Identifier);

/// <summary>
/// Data for exporting a single operation
/// </summary>
public class OperationExportData
{
    public string OperationType { get; set; } = "";
    public string Description { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public int Quantity { get; set; }
    public decimal PartPrice { get; set; }
    public decimal LaborHours { get; set; }
    public string LaborType { get; set; } = "";
    public decimal RefinishHours { get; set; }

    /// <summary>
    /// Create from an OperationRow
    /// </summary>
    public static OperationExportData FromOperationRow(OperationRow row)
    {
        return new OperationExportData
        {
            OperationType = row.OperationType,
            Description = row.Name,
            LaborHours = (decimal)row.Labor,
            RefinishHours = (decimal)row.Refinish,
            PartPrice = (decimal)row.Price,
            Quantity = row.Quantity > 0 ? row.Quantity : 1
        };
    }
}

/// <summary>
/// Result of exporting a single operation to CCC
/// </summary>
public class CCCExportResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public int FieldsSet { get; set; }
}

/// <summary>
/// Result of batch export to CCC
/// </summary>
public class CCCBatchExportResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public bool WasCancelled { get; set; }
    public string ErrorMessage { get; set; } = "";
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Progress information during CCC export
/// </summary>
public class CCCExportProgress
{
    public int CurrentItem { get; set; }
    public int TotalItems { get; set; }
    public string CurrentDescription { get; set; } = "";
    public int PercentComplete { get; set; }
}
