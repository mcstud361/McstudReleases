#nullable enable
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace McstudDesktop.Models;

public enum OperationType
{
    Repair,
    Replace,
    RemoveAndInstall,
    Refinish,
    Blend
}

public class Operation : INotifyPropertyChanged
{
    private string _description = string.Empty;
    private decimal _price;
    private decimal _laborHours;
    private decimal _refinishHours;
    private int _quantity = 1;
    private OperationType _operationType;
    private bool _isVisible = true;

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public decimal Price
    {
        get => _price;
        set { _price = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPrice)); }
    }

    public decimal LaborHours
    {
        get => _laborHours;
        set { _laborHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalLaborHours)); }
    }

    public decimal RefinishHours
    {
        get => _refinishHours;
        set { _refinishHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalRefinishHours)); }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPrice));
            OnPropertyChanged(nameof(TotalLaborHours));
            OnPropertyChanged(nameof(TotalRefinishHours));
        }
    }

    public OperationType OperationType
    {
        get => _operationType;
        set { _operationType = value; OnPropertyChanged(); OnPropertyChanged(nameof(OperationTypeString)); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public string Category { get; set; } = string.Empty;

    // Computed properties
    public decimal TotalPrice => Price * Quantity;
    public decimal TotalLaborHours => LaborHours * Quantity;
    public decimal TotalRefinishHours => RefinishHours * Quantity;

    public string OperationTypeString => OperationType switch
    {
        OperationType.Repair => "Rpr",
        OperationType.Replace => "Replace",
        OperationType.RemoveAndInstall => "R&I",
        OperationType.Refinish => "Refinish",
        OperationType.Blend => "Blend",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
