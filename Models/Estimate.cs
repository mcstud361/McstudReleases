#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace McStudDesktop.Models;

public class Estimate : INotifyPropertyChanged
{
    private string _customerName = string.Empty;
    private string _vehicleInfo = string.Empty;
    private DateTime _dateCreated = DateTime.Now;

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

    // Summary calculations
    public int TotalOperationsCount
    {
        get
        {
            return SOPOperations.Count(o => o.IsVisible) +
                   PartOperations.Count(o => o.IsVisible) +
                   CoverCarOperations.Count(o => o.IsVisible) +
                   BodyOperations.Count(o => o.IsVisible) +
                   RefinishOperations.Count(o => o.IsVisible) +
                   MechanicalOperations.Count(o => o.IsVisible) +
                   SRSOperations.Count(o => o.IsVisible) +
                   TotalLossOperations.Count(o => o.IsVisible) +
                   BodyOnFrameOperations.Count(o => o.IsVisible) +
                   StolenRecoveryOperations.Count(o => o.IsVisible);
        }
    }

    public decimal TotalPrice
    {
        get
        {
            return SOPOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   PartOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   CoverCarOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   BodyOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   RefinishOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   MechanicalOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   SRSOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   TotalLossOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   BodyOnFrameOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice) +
                   StolenRecoveryOperations.Where(o => o.IsVisible).Sum(o => o.TotalPrice);
        }
    }

    public decimal TotalLaborHours
    {
        get
        {
            return SOPOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   PartOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   CoverCarOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   BodyOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   RefinishOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   MechanicalOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   SRSOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   TotalLossOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   BodyOnFrameOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours) +
                   StolenRecoveryOperations.Where(o => o.IsVisible).Sum(o => o.TotalLaborHours);
        }
    }

    public decimal TotalRefinishHours
    {
        get
        {
            return SOPOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   PartOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   CoverCarOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   BodyOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   RefinishOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   MechanicalOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   SRSOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   TotalLossOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   BodyOnFrameOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours) +
                   StolenRecoveryOperations.Where(o => o.IsVisible).Sum(o => o.TotalRefinishHours);
        }
    }

    public void RefreshTotals()
    {
        OnPropertyChanged(nameof(TotalOperationsCount));
        OnPropertyChanged(nameof(TotalPrice));
        OnPropertyChanged(nameof(TotalLaborHours));
        OnPropertyChanged(nameof(TotalRefinishHours));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
