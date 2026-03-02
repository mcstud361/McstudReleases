using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using McStudDesktop.Services;

namespace McStudDesktop.ViewModels;

/// <summary>
/// ViewModel for Part Operations tab
/// Categories: Plastic Part (Blend/Repair/Replace), SMC Part (Blend/Repair/Replace),
/// Fiberglass (Blend/Replace), Chrome Part, Aluminum Part
/// </summary>
public class PartOperationsViewModel : INotifyPropertyChanged
{
    private readonly ExcelEngineService? _excelEngine;

    public PartOperationsViewModel(ExcelEngineService? excelEngine = null)
    {
        _excelEngine = excelEngine;
        InitializeDefaults();
    }

    #region Plastic Part Blend Properties

    private string _plasticBlendPanelType = "Additional Panel";
    public string PlasticBlendPanelType
    {
        get => _plasticBlendPanelType;
        set { _plasticBlendPanelType = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }
    public string[] PlasticBlendPanelTypeOptions => new[] { "First Panel", "Additional Panel" };

    private bool _plasticBlendEnabled = true;
    public bool PlasticBlendEnabled
    {
        get => _plasticBlendEnabled;
        set { _plasticBlendEnabled = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }

    private string _plasticBlendPartSize = "First Large Part";
    public string PlasticBlendPartSize
    {
        get => _plasticBlendPartSize;
        set { _plasticBlendPartSize = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }
    public string[] PlasticBlendPartSizeOptions => new[] { "First Large Part", "Additional Large Part", "Additional Small Part" };

    private string _plasticBlendPDREquipped = "Not Equipped";
    public string PlasticBlendPDREquipped
    {
        get => _plasticBlendPDREquipped;
        set { _plasticBlendPDREquipped = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }
    public string[] EquippedOptions => new[] { "Equipped", "Not Equipped" };

    private bool _plasticBlendPunchHoles = false;
    public bool PlasticBlendPunchHoles
    {
        get => _plasticBlendPunchHoles;
        set { _plasticBlendPunchHoles = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }

    private bool _plasticBlendInstallBrackets = false;
    public bool PlasticBlendInstallBrackets
    {
        get => _plasticBlendInstallBrackets;
        set { _plasticBlendInstallBrackets = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }

    private bool _plasticBlendRemove = false;
    public bool PlasticBlendRemove
    {
        get => _plasticBlendRemove;
        set { _plasticBlendRemove = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }

    private bool _plasticBlendApply = false;
    public bool PlasticBlendApply
    {
        get => _plasticBlendApply;
        set { _plasticBlendApply = value; OnPropertyChanged(); RecalculatePlasticBlend(); }
    }

    #endregion

    #region Plastic Part Repair Properties

    private string _plasticRepairPanelType = "First Panel";
    public string PlasticRepairPanelType
    {
        get => _plasticRepairPanelType;
        set { _plasticRepairPanelType = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private bool _plasticRepairEnabled = true;
    public bool PlasticRepairEnabled
    {
        get => _plasticRepairEnabled;
        set { _plasticRepairEnabled = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private string _plasticRepairPartSize = "First Large Part";
    public string PlasticRepairPartSize
    {
        get => _plasticRepairPartSize;
        set { _plasticRepairPartSize = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private string _plasticRepairPDREquipped = "Not Equipped";
    public string PlasticRepairPDREquipped
    {
        get => _plasticRepairPDREquipped;
        set { _plasticRepairPDREquipped = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private bool _plasticRepairPunchHoles = false;
    public bool PlasticRepairPunchHoles
    {
        get => _plasticRepairPunchHoles;
        set { _plasticRepairPunchHoles = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private bool _plasticRepairInstallBrackets = false;
    public bool PlasticRepairInstallBrackets
    {
        get => _plasticRepairInstallBrackets;
        set { _plasticRepairInstallBrackets = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private bool _plasticRepairRemove = false;
    public bool PlasticRepairRemove
    {
        get => _plasticRepairRemove;
        set { _plasticRepairRemove = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    private bool _plasticRepairApply = false;
    public bool PlasticRepairApply
    {
        get => _plasticRepairApply;
        set { _plasticRepairApply = value; OnPropertyChanged(); RecalculatePlasticRepair(); }
    }

    #endregion

    #region Plastic Part Replace Properties

    private string _plasticReplacePanelType = "Additional Panel";
    public string PlasticReplacePanelType
    {
        get => _plasticReplacePanelType;
        set { _plasticReplacePanelType = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    private bool _plasticReplaceEnabled = true;
    public bool PlasticReplaceEnabled
    {
        get => _plasticReplaceEnabled;
        set { _plasticReplaceEnabled = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    private string _plasticReplacePartSize = "Additional Large Part";
    public string PlasticReplacePartSize
    {
        get => _plasticReplacePartSize;
        set { _plasticReplacePartSize = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    private string _plasticReplacePDREquipped = "Not Equipped";
    public string PlasticReplacePDREquipped
    {
        get => _plasticReplacePDREquipped;
        set { _plasticReplacePDREquipped = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    private bool _plasticReplacePunchHoles = false;
    public bool PlasticReplacePunchHoles
    {
        get => _plasticReplacePunchHoles;
        set { _plasticReplacePunchHoles = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    private bool _plasticReplaceInstallBrackets = false;
    public bool PlasticReplaceInstallBrackets
    {
        get => _plasticReplaceInstallBrackets;
        set { _plasticReplaceInstallBrackets = value; OnPropertyChanged(); RecalculatePlasticReplace(); }
    }

    #endregion

    #region SMC Part Blend Properties

    private string _smcBlendPanelType = "First Panel";
    public string SmcBlendPanelType
    {
        get => _smcBlendPanelType;
        set { _smcBlendPanelType = value; OnPropertyChanged(); RecalculateSmcBlend(); }
    }

    private bool _smcBlendEnabled = true;
    public bool SmcBlendEnabled
    {
        get => _smcBlendEnabled;
        set { _smcBlendEnabled = value; OnPropertyChanged(); RecalculateSmcBlend(); }
    }

    #endregion

    #region SMC Part Repair Properties

    private string _smcRepairPanelType = "Additional Panel";
    public string SmcRepairPanelType
    {
        get => _smcRepairPanelType;
        set { _smcRepairPanelType = value; OnPropertyChanged(); RecalculateSmcRepair(); }
    }
    public string[] SmcPanelTypeOptions => new[] { "First Panel", "Additional Panel", "First Panel Facing Sky" };

    private bool _smcRepairEnabled = true;
    public bool SmcRepairEnabled
    {
        get => _smcRepairEnabled;
        set { _smcRepairEnabled = value; OnPropertyChanged(); RecalculateSmcRepair(); }
    }

    private string _smcRepairPDREquipped = "Not Equipped";
    public string SmcRepairPDREquipped
    {
        get => _smcRepairPDREquipped;
        set { _smcRepairPDREquipped = value; OnPropertyChanged(); RecalculateSmcRepair(); }
    }

    private bool _smcRepairRemove = false;
    public bool SmcRepairRemove
    {
        get => _smcRepairRemove;
        set { _smcRepairRemove = value; OnPropertyChanged(); RecalculateSmcRepair(); }
    }

    private bool _smcRepairApply = false;
    public bool SmcRepairApply
    {
        get => _smcRepairApply;
        set { _smcRepairApply = value; OnPropertyChanged(); RecalculateSmcRepair(); }
    }

    #endregion

    #region SMC Part Replace Properties

    private string _smcReplacePanelType = "Additional Panel";
    public string SmcReplacePanelType
    {
        get => _smcReplacePanelType;
        set { _smcReplacePanelType = value; OnPropertyChanged(); RecalculateSmcReplace(); }
    }

    private bool _smcReplaceEnabled = false;
    public bool SmcReplaceEnabled
    {
        get => _smcReplaceEnabled;
        set { _smcReplaceEnabled = value; OnPropertyChanged(); RecalculateSmcReplace(); }
    }

    private string _smcReplacePDREquipped = "Not Equipped";
    public string SmcReplacePDREquipped
    {
        get => _smcReplacePDREquipped;
        set { _smcReplacePDREquipped = value; OnPropertyChanged(); RecalculateSmcReplace(); }
    }

    private bool _smcReplacePunchHoles = false;
    public bool SmcReplacePunchHoles
    {
        get => _smcReplacePunchHoles;
        set { _smcReplacePunchHoles = value; OnPropertyChanged(); RecalculateSmcReplace(); }
    }

    private bool _smcReplaceInstallBrackets = false;
    public bool SmcReplaceInstallBrackets
    {
        get => _smcReplaceInstallBrackets;
        set { _smcReplaceInstallBrackets = value; OnPropertyChanged(); RecalculateSmcReplace(); }
    }

    #endregion

    #region Fiberglass Blend Properties

    private string _fiberglassBlendPanelType = "First Panel Facing Sky";
    public string FiberglassBlendPanelType
    {
        get => _fiberglassBlendPanelType;
        set { _fiberglassBlendPanelType = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendEnabled = false;
    public bool FiberglassBlendEnabled
    {
        get => _fiberglassBlendEnabled;
        set { _fiberglassBlendEnabled = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private string _fiberglassBlendPDREquipped = "Not Equipped";
    public string FiberglassBlendPDREquipped
    {
        get => _fiberglassBlendPDREquipped;
        set { _fiberglassBlendPDREquipped = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendPunchHoles = false;
    public bool FiberglassBlendPunchHoles
    {
        get => _fiberglassBlendPunchHoles;
        set { _fiberglassBlendPunchHoles = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendInstallBrackets = false;
    public bool FiberglassBlendInstallBrackets
    {
        get => _fiberglassBlendInstallBrackets;
        set { _fiberglassBlendInstallBrackets = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendRemove = false;
    public bool FiberglassBlendRemove
    {
        get => _fiberglassBlendRemove;
        set { _fiberglassBlendRemove = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendApply = false;
    public bool FiberglassBlendApply
    {
        get => _fiberglassBlendApply;
        set { _fiberglassBlendApply = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    private bool _fiberglassBlendDualPinstripes = false;
    public bool FiberglassBlendDualPinstripes
    {
        get => _fiberglassBlendDualPinstripes;
        set { _fiberglassBlendDualPinstripes = value; OnPropertyChanged(); RecalculateFiberglassBlend(); }
    }

    #endregion

    #region Fiberglass Replace Properties

    private string _fiberglassReplacePanelType = "First Panel";
    public string FiberglassReplacePanelType
    {
        get => _fiberglassReplacePanelType;
        set { _fiberglassReplacePanelType = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplaceEnabled = true;
    public bool FiberglassReplaceEnabled
    {
        get => _fiberglassReplaceEnabled;
        set { _fiberglassReplaceEnabled = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private string _fiberglassReplacePDREquipped = "Not Equipped";
    public string FiberglassReplacePDREquipped
    {
        get => _fiberglassReplacePDREquipped;
        set { _fiberglassReplacePDREquipped = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplacePunchHoles = false;
    public bool FiberglassReplacePunchHoles
    {
        get => _fiberglassReplacePunchHoles;
        set { _fiberglassReplacePunchHoles = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplaceInstallBrackets = false;
    public bool FiberglassReplaceInstallBrackets
    {
        get => _fiberglassReplaceInstallBrackets;
        set { _fiberglassReplaceInstallBrackets = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplaceRemove = false;
    public bool FiberglassReplaceRemove
    {
        get => _fiberglassReplaceRemove;
        set { _fiberglassReplaceRemove = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplaceApply = false;
    public bool FiberglassReplaceApply
    {
        get => _fiberglassReplaceApply;
        set { _fiberglassReplaceApply = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    private bool _fiberglassReplaceInstallStickers = false;
    public bool FiberglassReplaceInstallStickers
    {
        get => _fiberglassReplaceInstallStickers;
        set { _fiberglassReplaceInstallStickers = value; OnPropertyChanged(); RecalculateFiberglassReplace(); }
    }

    #endregion

    #region Chrome Part Properties

    private string _chromePartSize = "Large";
    public string ChromePartSize
    {
        get => _chromePartSize;
        set { _chromePartSize = value; OnPropertyChanged(); RecalculateChromePart(); }
    }
    public string[] ChromePartSizeOptions => new[] { "Large", "Small" };

    private bool _chromePartEnabled = true;
    public bool ChromePartEnabled
    {
        get => _chromePartEnabled;
        set { _chromePartEnabled = value; OnPropertyChanged(); RecalculateChromePart(); }
    }

    #endregion

    #region Aluminum Part Properties

    private string _aluminumPartSize = "Large";
    public string AluminumPartSize
    {
        get => _aluminumPartSize;
        set { _aluminumPartSize = value; OnPropertyChanged(); RecalculateAluminumPart(); }
    }

    private bool _aluminumPartEnabled = false;
    public bool AluminumPartEnabled
    {
        get => _aluminumPartEnabled;
        set { _aluminumPartEnabled = value; OnPropertyChanged(); RecalculateAluminumPart(); }
    }

    #endregion

    #region Operations Collections

    // Plastic categories
    public ObservableCollection<OperationRow> PlasticBlendOperations { get; } = new();
    public ObservableCollection<OperationRow> PlasticRepairOperations { get; } = new();
    public ObservableCollection<OperationRow> PlasticReplaceOperations { get; } = new();

    // Carbon Fiber / SMC / Composite (consolidated)
    public ObservableCollection<OperationRow> SmcOperations { get; } = new();

    // Metal categories
    public ObservableCollection<OperationRow> MetalBlendOperations { get; } = new();
    public ObservableCollection<OperationRow> MetalRepairOperations { get; } = new();
    public ObservableCollection<OperationRow> BoltedMetalOperations { get; } = new();
    public ObservableCollection<OperationRow> WeldedMetalOperations { get; } = new();
    public ObservableCollection<OperationRow> InnerPanelOperations { get; } = new();

    // Glass
    public ObservableCollection<OperationRow> GlassOperations { get; } = new();

    // Legacy collections (kept for compatibility)
    public ObservableCollection<OperationRow> SmcBlendOperations { get; } = new();
    public ObservableCollection<OperationRow> SmcRepairOperations { get; } = new();
    public ObservableCollection<OperationRow> SmcReplaceOperations { get; } = new();
    public ObservableCollection<OperationRow> MetalReplaceOperations { get; } = new();
    public ObservableCollection<OperationRow> FiberglassBlendOperations { get; } = new();
    public ObservableCollection<OperationRow> FiberglassReplaceOperations { get; } = new();
    public ObservableCollection<OperationRow> ChromePartOperations { get; } = new();
    public ObservableCollection<OperationRow> AluminumPartOperations { get; } = new();

    // CurrentOperations points to the currently selected category's operations
    private ObservableCollection<OperationRow> _currentOperations = new();
    public ObservableCollection<OperationRow> CurrentOperations
    {
        get => _currentOperations;
        set { _currentOperations = value; OnPropertyChanged(); }
    }

    #endregion

    #region Footer Totals

    private string _currentCategoryName = "Plastic Part Blend";
    public string CurrentCategoryName
    {
        get => _currentCategoryName;
        set { _currentCategoryName = value; OnPropertyChanged(); }
    }

    private int _currentCategoryOperations;
    public int CurrentCategoryOperations
    {
        get => _currentCategoryOperations;
        set { _currentCategoryOperations = value; OnPropertyChanged(); }
    }

    private string _currentCategoryPriceFormatted = "0.00";
    public string CurrentCategoryPriceFormatted
    {
        get => _currentCategoryPriceFormatted;
        set { _currentCategoryPriceFormatted = value; OnPropertyChanged(); }
    }

    private string _currentCategoryLaborFormatted = "0.0";
    public string CurrentCategoryLaborFormatted
    {
        get => _currentCategoryLaborFormatted;
        set { _currentCategoryLaborFormatted = value; OnPropertyChanged(); }
    }

    private string _currentCategoryRefinishFormatted = "0.0";
    public string CurrentCategoryRefinishFormatted
    {
        get => _currentCategoryRefinishFormatted;
        set { _currentCategoryRefinishFormatted = value; OnPropertyChanged(); }
    }

    #endregion

    #region Initialization

    private void InitializeDefaults()
    {
        RecalculateAllOperations();
    }

    private void RecalculateAllOperations()
    {
        RecalculatePlasticBlend();
        RecalculatePlasticRepair();
        RecalculatePlasticReplace();
        RecalculateSmcBlend();
        RecalculateSmcRepair();
        RecalculateSmcReplace();
        RecalculateFiberglassBlend();
        RecalculateFiberglassReplace();
        RecalculateChromePart();
        RecalculateAluminumPart();
    }

    #endregion

    #region Calculation Methods

    private void RecalculatePlasticBlend()
    {
        PlasticBlendOperations.Clear();
        if (!_plasticBlendEnabled) return;

        PlasticBlendOperations.Add(new OperationRow
        {
            OperationType = "Rpr",
            Name = $"Plastic Part Blend - {_plasticBlendPanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _plasticBlendPanelType == "First Panel" ? 1.5 : 1.0,
            Category = "B",
            Refinish = _plasticBlendPanelType == "First Panel" ? 2.0 : 1.5
        });

        if (_plasticBlendPunchHoles)
            PlasticBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_plasticBlendInstallBrackets)
            PlasticBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_plasticBlendRemove)
            PlasticBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Remove Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_plasticBlendApply)
            PlasticBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Apply Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculatePlasticRepair()
    {
        PlasticRepairOperations.Clear();
        if (!_plasticRepairEnabled) return;

        PlasticRepairOperations.Add(new OperationRow
        {
            OperationType = "Rpr",
            Name = $"Plastic Part Repair - {_plasticRepairPanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _plasticRepairPanelType == "First Panel" ? 2.0 : 1.5,
            Category = "B",
            Refinish = 2.0
        });

        if (_plasticRepairPunchHoles)
            PlasticRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_plasticRepairInstallBrackets)
            PlasticRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_plasticRepairRemove)
            PlasticRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Remove Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_plasticRepairApply)
            PlasticRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Apply Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculatePlasticReplace()
    {
        PlasticReplaceOperations.Clear();
        if (!_plasticReplaceEnabled) return;

        PlasticReplaceOperations.Add(new OperationRow
        {
            OperationType = "Rpl",
            Name = $"Plastic Part Replace - {_plasticReplacePanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _plasticReplacePanelType == "First Panel" ? 1.0 : 0.5,
            Category = "B",
            Refinish = 2.0
        });

        if (_plasticReplacePunchHoles)
            PlasticReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_plasticReplaceInstallBrackets)
            PlasticReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculateSmcBlend()
    {
        SmcBlendOperations.Clear();
        if (!_smcBlendEnabled) return;

        SmcBlendOperations.Add(new OperationRow
        {
            OperationType = "Rpr",
            Name = $"SMC Part Blend - {_smcBlendPanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _smcBlendPanelType == "First Panel" ? 1.5 : 1.0,
            Category = "B",
            Refinish = 1.5
        });
    }

    private void RecalculateSmcRepair()
    {
        SmcRepairOperations.Clear();
        if (!_smcRepairEnabled) return;

        SmcRepairOperations.Add(new OperationRow
        {
            OperationType = "Rpr",
            Name = $"SMC Part Repair - {_smcRepairPanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _smcRepairPanelType == "First Panel" ? 2.0 : 1.5,
            Category = "B",
            Refinish = 2.0
        });

        if (_smcRepairRemove)
            SmcRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Remove Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_smcRepairApply)
            SmcRepairOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Apply Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculateSmcReplace()
    {
        SmcReplaceOperations.Clear();
        if (!_smcReplaceEnabled) return;

        SmcReplaceOperations.Add(new OperationRow
        {
            OperationType = "Rpl",
            Name = $"SMC Part Replace - {_smcReplacePanelType}",
            Quantity = 1,
            Price = 0,
            Labor = 0.5,
            Category = "B",
            Refinish = 2.0
        });

        if (_smcReplacePunchHoles)
            SmcReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_smcReplaceInstallBrackets)
            SmcReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculateFiberglassBlend()
    {
        FiberglassBlendOperations.Clear();
        if (!_fiberglassBlendEnabled) return;

        FiberglassBlendOperations.Add(new OperationRow
        {
            OperationType = "Rpr",
            Name = $"Fiberglass Blend - {_fiberglassBlendPanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _fiberglassBlendPanelType == "First Panel" ? 1.5 : 1.0,
            Category = "B",
            Refinish = 1.5
        });

        if (_fiberglassBlendPunchHoles)
            FiberglassBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_fiberglassBlendInstallBrackets)
            FiberglassBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassBlendRemove)
            FiberglassBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Remove Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassBlendApply)
            FiberglassBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Apply Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassBlendDualPinstripes)
            FiberglassBlendOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Dual Pinstripes", Quantity = 1, Price = 0, Labor = 0.3, Category = "R", Refinish = 0.3 });
    }

    private void RecalculateFiberglassReplace()
    {
        FiberglassReplaceOperations.Clear();
        if (!_fiberglassReplaceEnabled) return;

        FiberglassReplaceOperations.Add(new OperationRow
        {
            OperationType = "Rpl",
            Name = $"Fiberglass Replace - {_fiberglassReplacePanelType}",
            Quantity = 1,
            Price = 0,
            Labor = _fiberglassReplacePanelType == "First Panel" ? 1.0 : 0.5,
            Category = "B",
            Refinish = 2.0
        });

        if (_fiberglassReplacePunchHoles)
            FiberglassReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Punch Holes", Quantity = 1, Price = 0, Labor = 0.3, Category = "B", Refinish = 0 });

        if (_fiberglassReplaceInstallBrackets)
            FiberglassReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install Brackets", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassReplaceRemove)
            FiberglassReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Remove Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassReplaceApply)
            FiberglassReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Apply Decal/Sticker", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });

        if (_fiberglassReplaceInstallStickers)
            FiberglassReplaceOperations.Add(new OperationRow { OperationType = "Rpr", Name = "Install New Stickers", Quantity = 1, Price = 0, Labor = 0.2, Category = "B", Refinish = 0 });
    }

    private void RecalculateChromePart()
    {
        ChromePartOperations.Clear();
        if (!_chromePartEnabled) return;

        ChromePartOperations.Add(new OperationRow
        {
            OperationType = "Rpl",
            Name = $"Chrome Part - {_chromePartSize}",
            Quantity = 1,
            Price = 0,
            Labor = _chromePartSize == "Large" ? 0.5 : 0.3,
            Category = "B",
            Refinish = 0
        });
    }

    private void RecalculateAluminumPart()
    {
        AluminumPartOperations.Clear();
        if (!_aluminumPartEnabled) return;

        AluminumPartOperations.Add(new OperationRow
        {
            OperationType = "Rpl",
            Name = $"Aluminum Part - {_aluminumPartSize}",
            Quantity = 1,
            Price = 0,
            Labor = _aluminumPartSize == "Large" ? 0.8 : 0.4,
            Category = "B",
            Refinish = 0
        });
    }

    #endregion

    #region Footer Update

    public void UpdateCurrentCategoryTotals(string categoryName)
    {
        CurrentCategoryName = categoryName;

        var operations = categoryName switch
        {
            "Plastic Part Blend" => PlasticBlendOperations,
            "Plastic Part Repair" => PlasticRepairOperations,
            "Plastic Part Replace" => PlasticReplaceOperations,
            "Metal Part Blend" => MetalBlendOperations,
            "Metal Part Repair" => MetalRepairOperations,
            "Bolted Metal Replace" => MetalReplaceOperations,
            "Glass" => GlassOperations,
            "SMC Part Blend" => SmcBlendOperations,
            "SMC Part Repair" => SmcRepairOperations,
            "SMC Part Replace" => SmcReplaceOperations,
            "Fiberglass Blend" => FiberglassBlendOperations,
            "Fiberglass Replace" => FiberglassReplaceOperations,
            "Chrome Part" => ChromePartOperations,
            "Aluminum Part" => AluminumPartOperations,
            _ => PlasticBlendOperations
        };

        // Update CurrentOperations to point to the selected category
        CurrentOperations.Clear();
        foreach (var op in operations)
        {
            CurrentOperations.Add(op);
        }

        CurrentCategoryOperations = operations.Count;

        double totalPrice = 0;
        double totalLabor = 0;
        double totalRefinish = 0;

        foreach (var op in operations)
        {
            totalPrice += op.Price;
            totalLabor += op.Labor;
            totalRefinish += op.Refinish;
        }

        CurrentCategoryPriceFormatted = totalPrice.ToString("F2");
        CurrentCategoryLaborFormatted = totalLabor.ToString("F1");
        CurrentCategoryRefinishFormatted = totalRefinish.ToString("F1");
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
