#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using McstudDesktop.Models;

namespace McStudDesktop.Services
{
    /// <summary>
    /// Strategy interface for estimating system automation.
    /// Each estimating system (CCC Desktop, CCC Web, Mitchell) implements this.
    /// </summary>
    public interface IEstimatingSystemAdapter
    {
        string SystemName { get; }
        bool SupportsElementDiscovery { get; }
        bool IsConnected { get; }

        bool CanHandle(OcrEstimateSource source);
        Task<bool> ConnectAsync(CancellationToken ct = default);
        Task<bool> FocusEstimatingWindowAsync(CancellationToken ct = default);
        Task<bool> InsertNewLineAsync(CancellationToken ct = default);
        Task<bool> TypeInFieldAsync(string value, CancellationToken ct = default);
        Task<bool> TabToNextFieldAsync(CancellationToken ct = default);
        Task<bool> PressEnterAsync(CancellationToken ct = default);
        Task<bool> PressEscapeAsync(CancellationToken ct = default);
        Task<bool> PressKeyAsync(string key, CancellationToken ct = default);
        Task<bool> ClickElementAsync(string elementText, CancellationToken ct = default);
        Task<ScreenOcrResult?> ReadCurrentScreenAsync(CancellationToken ct = default);

        event EventHandler<string>? StatusChanged;
        event EventHandler<AutomationProgress>? ProgressChanged;
    }

    #region Automation Models

    public class AutomationProgress
    {
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public string Description { get; set; } = "";
        public double PercentComplete => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
    }

    public class AutomationIntent
    {
        public string IntentType { get; set; } = "add_operation";
        public List<AutomationOperation> Operations { get; set; } = new();
        public double Confidence { get; set; }
        public string Explanation { get; set; } = "";
        public string RawCommand { get; set; } = "";
    }

    public class AutomationOperation
    {
        public string Part { get; set; } = "";
        public string OperationType { get; set; } = "";
        public decimal? Hours { get; set; }
        public string? PartNumber { get; set; }
    }

    public class AutomationPlan
    {
        public AutomationIntent Intent { get; set; } = new();
        public List<AutomationStep> Steps { get; set; } = new();
        public string SystemName { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public class AutomationStep
    {
        public int StepNumber { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Value { get; set; }
    }

    public class AutomationResult
    {
        public bool Success { get; set; }
        public int StepsCompleted { get; set; }
        public int TotalSteps { get; set; }
        public string Message { get; set; } = "";
        public bool WasCancelled { get; set; }
        public bool OcrVerified { get; set; }
        public string? OcrVerificationDetail { get; set; }
    }

    #endregion
}
