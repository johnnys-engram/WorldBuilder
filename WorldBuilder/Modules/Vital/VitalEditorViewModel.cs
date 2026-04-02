using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Vital;

public partial class VitalEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private VitalTable? _vitalTable;
    private bool _initialized;
    private const uint VitalTableId = 0x0E000003;

    [ObservableProperty] private string _statusText = "Loading vital editor…";

    [ObservableProperty] private SkillFormulaViewModel? _healthFormula;
    [ObservableProperty] private SkillFormulaViewModel? _staminaFormula;
    [ObservableProperty] private SkillFormulaViewModel? _manaFormula;
    [ObservableProperty] private bool _isVitalEditingEnabled;

    public WorldBuilderSettings Settings { get; }

    public VitalEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
        IDatReaderWriter dats, IDialogService dialogService) {
        Settings = settings;
        _project = project;
        _documentManager = documentManager;
        _dats = dats;
        _ = dialogService;
    }

    public async Task InitializeAsync(CancellationToken ct = default) {
        if (_initialized) return;

        if (_project.IsReadOnly) {
            StatusText = "Read-only project — viewing vital table only; saving is disabled.";
            LoadVitalTableReadOnly();
            IsVitalEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsVitalEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadVitalTable();
        IsVitalEditingEnabled = true;
        _initialized = true;
    }

    private void LoadVitalTableReadOnly() {
        if (!_dats.Portal.TryGet<VitalTable>(VitalTableId, out var datTable)) {
            StatusText = "Failed to load VitalTable from DAT";
            return;
        }

        _vitalTable = datTable;
        HealthFormula = new SkillFormulaViewModel("Health", _vitalTable.Health);
        StaminaFormula = new SkillFormulaViewModel("Stamina", _vitalTable.Stamina);
        ManaFormula = new SkillFormulaViewModel("Mana", _vitalTable.Mana);
        StatusText = "Loaded VitalTable (0x0E000003) (read-only)";
    }

    private void LoadVitalTable() {
        if (_portalDoc != null && _portalDoc.TryGetEntry<VitalTable>(VitalTableId, out var docTable) && docTable != null) {
            _vitalTable = docTable;
        }
        else if (!_dats.Portal.TryGet<VitalTable>(VitalTableId, out var datTable)) {
            StatusText = "Failed to load VitalTable from DAT";
            return;
        }
        else {
            _vitalTable = datTable;
        }

        HealthFormula = new SkillFormulaViewModel("Health", _vitalTable.Health);
        StaminaFormula = new SkillFormulaViewModel("Stamina", _vitalTable.Stamina);
        ManaFormula = new SkillFormulaViewModel("Mana", _vitalTable.Mana);

        StatusText = "Loaded VitalTable (0x0E000003)";
    }

    private async Task PersistPortalAsync(CancellationToken ct) {
        if (_portalRental == null) return;
        _portalRental.Document.Version++;
        var persist = await _documentManager.PersistDocumentAsync(_portalRental, null, ct);
        if (persist.IsFailure) {
            StatusText = $"Save to project failed: {persist.Error.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct) {
        if (!IsVitalEditingEnabled || _vitalTable == null || _portalDoc == null) return;

        HealthFormula?.ApplyTo(_vitalTable.Health);
        StaminaFormula?.ApplyTo(_vitalTable.Stamina);
        ManaFormula?.ApplyTo(_vitalTable.Mana);

        _portalDoc.SetEntry(VitalTableId, _vitalTable);
        await PersistPortalAsync(ct);
        StatusText = "Saved VitalTable to project. Use File → Export Dats to write client_portal.dat.";
    }
}

public partial class SkillFormulaViewModel : ObservableObject {
    public string VitalName { get; }

    [ObservableProperty] private int _unknown;
    [ObservableProperty] private bool _hasSecondAttribute;
    [ObservableProperty] private bool _useFormula;
    [ObservableProperty] private int _divisor;
    [ObservableProperty] private AttributeId _attribute1;
    [ObservableProperty] private AttributeId _attribute2;

    public IReadOnlyList<AttributeId> AllAttributes { get; } = Enum.GetValues<AttributeId>();

    public string FormulaDisplay => HasSecondAttribute
        ? $"({Attribute1} + {Attribute2}) / {Divisor}"
        : $"{Attribute1} / {Divisor}";

    partial void OnHasSecondAttributeChanged(bool value) => OnPropertyChanged(nameof(FormulaDisplay));
    partial void OnAttribute1Changed(AttributeId value) => OnPropertyChanged(nameof(FormulaDisplay));
    partial void OnAttribute2Changed(AttributeId value) => OnPropertyChanged(nameof(FormulaDisplay));
    partial void OnDivisorChanged(int value) => OnPropertyChanged(nameof(FormulaDisplay));

    public SkillFormulaViewModel(string vitalName, SkillFormula formula) {
        VitalName = vitalName;
        Divisor = formula.Divisor;
        Attribute1 = formula.Attribute1;
        Attribute2 = formula.Attribute2;
        UseFormula = formula.Attribute1Multiplier > 0;
        HasSecondAttribute = formula.Attribute2Multiplier > 0;
        Unknown = formula.AdditiveBonus;
    }

    public void ApplyTo(SkillFormula formula) {
        formula.Divisor = Divisor;
        formula.Attribute1 = Attribute1;
        formula.Attribute2 = Attribute2;
        formula.Attribute1Multiplier = UseFormula ? 1 : 0;
        formula.Attribute2Multiplier = HasSecondAttribute ? 1 : 0;
        formula.AdditiveBonus = Unknown;
    }
}
