#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace McstudDesktop.Models;

/// <summary>
/// Represents an estimate with operations across multiple categories.
/// Optimized with cached totals and single-pass aggregation.
/// </summary>
public class Estimate : INotifyPropertyChanged
{
    private string _customerName = string.Empty;
    private string _vehicleInfo = string.Empty;
    private DateTime _dateCreated = DateTime.Now;

    // Cached totals - invalidated when RefreshTotals is called
    private int? _cachedOperationsCount;
    private decimal? _cachedPrice;
    private decimal? _cachedLaborHours;
    private decimal? _cachedRefinishHours;

    public string CustomerName
    {
        get => _customerName;
        set { _customerName = value; OnPropertyChanged(); }
    }

    public string VehicleInfo
    {
        get => _vehicleInfo;
        set { _vehicleInfo = value; OnPropertyChanged(); }
    }

    public DateTime DateCreated
    {
        get => _dateCreated;
        set { _dateCreated = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Operation> SOPOperations { get; } = new();
    public ObservableCollection<Operation> PartOperations { get; } = new();
    public ObservableCollection<Operation> CoverCarOperations { get; } = new();
    public ObservableCollection<Operation> BodyOperations { get; } = new();
    public ObservableCollection<Operation> RefinishOperations { get; } = new();
    public ObservableCollection<Operation> MechanicalOperations { get; } = new();
    public ObservableCollection<Operation> SRSOperations { get; } = new();
    public ObservableCollection<Operation> TotalLossOperations { get; } = new();
    public ObservableCollection<Operation> BodyOnFrameOperations { get; } = new();
    public ObservableCollection<Operation> StolenRecoveryOperations { get; } = new();

    // All collections for iteration
    private IEnumerable<ObservableCollection<Operation>> AllCollections =>
    [
        SOPOperations, PartOperations, CoverCarOperations, BodyOperations,
        RefinishOperations, MechanicalOperations, SRSOperations,
        TotalLossOperations, BodyOnFrameOperations, StolenRecoveryOperations
    ];

    /// <summary>
    /// Calculate all totals in a single pass (more efficient than separate LINQ queries)
    /// </summary>
    private (int count, decimal price, decimal labor, decimal refinish) CalculateAllTotals()
    {
        int totalCount = 0;
        decimal totalPrice = 0m;
        decimal totalLabor = 0m;
        decimal totalRefinish = 0m;

        foreach (var collection in AllCollections)
        {
            foreach (var op in collection)
            {
                if (op.IsVisible)
                {
                    totalCount++;
                    totalPrice += op.TotalPrice;
                    totalLabor += op.TotalLaborHours;
                    totalRefinish += op.TotalRefinishHours;
                }
            }
        }

        return (totalCount, totalPrice, totalLabor, totalRefinish);
    }

    /// <summary>
    /// Ensure cached values are calculated
    /// </summary>
    private void EnsureCacheValid()
    {
        if (_cachedOperationsCount.HasValue)
            return;

        var (count, price, labor, refinish) = CalculateAllTotals();
        _cachedOperationsCount = count;
        _cachedPrice = price;
        _cachedLaborHours = labor;
        _cachedRefinishHours = refinish;
    }

    // Summary calculations with caching
    public int TotalOperationsCount
    {
        get
        {
            EnsureCacheValid();
            return _cachedOperationsCount!.Value;
        }
    }

    public decimal TotalPrice
    {
        get
        {
            EnsureCacheValid();
            return _cachedPrice!.Value;
        }
    }

    public decimal TotalLaborHours
    {
        get
        {
            EnsureCacheValid();
            return _cachedLaborHours!.Value;
        }
    }

    public decimal TotalRefinishHours
    {
        get
        {
            EnsureCacheValid();
            return _cachedRefinishHours!.Value;
        }
    }

    /// <summary>
    /// Invalidate cached totals and notify property changes
    /// </summary>
    public void RefreshTotals()
    {
        // Invalidate cache
        _cachedOperationsCount = null;
        _cachedPrice = null;
        _cachedLaborHours = null;
        _cachedRefinishHours = null;

        OnPropertyChanged(nameof(TotalOperationsCount));
        OnPropertyChanged(nameof(TotalPrice));
        OnPropertyChanged(nameof(TotalLaborHours));
        OnPropertyChanged(nameof(TotalRefinishHours));
    }

    /// <summary>
    /// Get a summary tuple of all totals in one call (avoids multiple property access)
    /// </summary>
    public (int Count, decimal Price, decimal Labor, decimal Refinish) GetTotalsSummary()
    {
        EnsureCacheValid();
        return (_cachedOperationsCount!.Value, _cachedPrice!.Value,
                _cachedLaborHours!.Value, _cachedRefinishHours!.Value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
