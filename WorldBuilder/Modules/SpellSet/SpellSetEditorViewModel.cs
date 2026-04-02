using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using HanumanInstitute.MvvmDialogs;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using SpellBase = DatReaderWriter.Types.SpellBase;

namespace WorldBuilder.Modules.SpellSet;

public partial class SpellSetEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private SpellTable? _spellTable;
    private bool _initialized;
    private const uint SpellTableId = 0x0E00000E;

    [ObservableProperty] private string _statusText = "Loading spell set editor…";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<SpellSetListItem> _spellSets = new();
    [ObservableProperty] private SpellSetListItem? _selectedSpellSet;
    [ObservableProperty] private SpellSetDetailViewModel? _selectedDetail;
    [ObservableProperty] private int _totalSetCount;
    [ObservableProperty] private int _filteredSetCount;
    [ObservableProperty] private bool _isSpellSetEditingEnabled;

    public WorldBuilderSettings Settings { get; }

    public SpellSetEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
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
            StatusText = "Read-only project — viewing spell sets only; saving is disabled.";
            LoadSpellSetsReadOnly();
            IsSpellSetEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsSpellSetEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadSpellSets();
        IsSpellSetEditingEnabled = true;
        _initialized = true;
    }

    private void LoadSpellSetsReadOnly() {
        if (!_dats.Portal.TryGet<SpellTable>(SpellTableId, out var datTable)) {
            StatusText = "Failed to load SpellTable from DAT";
            return;
        }

        _spellTable = datTable;
        TotalSetCount = _spellTable.SpellsSets.Count;
        ApplyFilter();
        StatusText = $"Loaded {TotalSetCount} spell sets (read-only), {_spellTable.Spells.Count} spells available";
    }

    private void LoadSpellSets() {
        if (_portalDoc != null && _portalDoc.TryGetEntry<SpellTable>(SpellTableId, out var docTable) && docTable != null) {
            _spellTable = docTable;
        }
        else if (!_dats.Portal.TryGet<SpellTable>(SpellTableId, out var datTable)) {
            StatusText = "Failed to load SpellTable from DAT";
            return;
        }
        else {
            _spellTable = datTable;
        }

        TotalSetCount = _spellTable.SpellsSets.Count;
        ApplyFilter();
        StatusText = $"Loaded {TotalSetCount} spell sets, {_spellTable.Spells.Count} spells available";
    }

    private async Task PersistPortalAsync(CancellationToken ct) {
        if (_portalRental == null) return;
        _portalRental.Document.Version++;
        var persist = await _documentManager.PersistDocumentAsync(_portalRental, null, ct);
        if (persist.IsFailure) {
            StatusText = $"Save to project failed: {persist.Error.Message}";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSpellSetChanged(SpellSetListItem? value) {
        if (value != null && _spellTable != null &&
            _spellTable.SpellsSets.TryGetValue(value.EquipmentSet, out var spellSet)) {
            SelectedDetail = new SpellSetDetailViewModel(value.EquipmentSet, spellSet, _spellTable.Spells);
        }
        else {
            SelectedDetail = null;
        }
    }

    private void ApplyFilter() {
        if (_spellTable == null) return;

        var query = SearchText?.Trim() ?? "";
        var filtered = _spellTable.SpellsSets
            .Where(kvp => {
                if (string.IsNullOrEmpty(query)) return true;
                return kvp.Key.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(kvp => kvp.Key.ToString())
            .Select(kvp => new SpellSetListItem(kvp.Key, kvp.Value))
            .ToList();

        SpellSets = new ObservableCollection<SpellSetListItem>(filtered);
        FilteredSetCount = filtered.Count;
    }

    [RelayCommand]
    private void ClearFilter() {
        SearchText = "";
    }

    [RelayCommand]
    private void AddTier() {
        if (!IsSpellSetEditingEnabled || SelectedDetail == null) return;
        SelectedDetail.AddNewTier();
        StatusText = $"Added tier to {SelectedDetail.EquipmentSet}";
    }

    [RelayCommand]
    private void RemoveTier() {
        if (!IsSpellSetEditingEnabled || SelectedDetail == null || SelectedDetail.Tiers.Count == 0) return;
        SelectedDetail.Tiers.RemoveAt(SelectedDetail.Tiers.Count - 1);
        StatusText = $"Removed last tier from {SelectedDetail.EquipmentSet}";
    }

    [RelayCommand]
    private void AddSpellSet() {
        if (!IsSpellSetEditingEnabled || _spellTable == null) return;

        int nextId = 1;
        if (_spellTable.SpellsSets.Count > 0)
            nextId = _spellTable.SpellsSets.Keys.Max(k => (int)k) + 1;

        var newSet = new DatReaderWriter.Types.SpellSet();
        var eqSet = (EquipmentSet)nextId;
        _spellTable.SpellsSets[eqSet] = newSet;
        TotalSetCount = _spellTable.SpellsSets.Count;
        ApplyFilter();

        SelectedSpellSet = SpellSets.FirstOrDefault(s => s.EquipmentSet == eqSet);
        StatusText = $"Added new spell set: {eqSet} (ID {nextId}). Remember to Save.";
    }

    [RelayCommand]
    private async Task SaveSpellSetAsync(CancellationToken ct) {
        if (!IsSpellSetEditingEnabled || SelectedDetail == null || _spellTable == null || _portalDoc == null) return;

        var eqSet = SelectedDetail.EquipmentSet;

        if (!_spellTable.SpellsSets.TryGetValue(eqSet, out var spellSet)) {
            spellSet = new DatReaderWriter.Types.SpellSet();
            _spellTable.SpellsSets[eqSet] = spellSet;
        }

        SelectedDetail.ApplyTo(spellSet);

        _portalDoc.SetEntry(SpellTableId, _spellTable);
        await PersistPortalAsync(ct);

        int idx = -1;
        for (int i = 0; i < SpellSets.Count; i++) {
            if (SpellSets[i].EquipmentSet == eqSet) { idx = i; break; }
        }
        if (idx >= 0) {
            SpellSets[idx] = new SpellSetListItem(eqSet, spellSet);
        }

        StatusText = $"Saved spell set: {eqSet} to project. Use File → Export Dats to write client_portal.dat.";
    }
}

public class SpellSetListItem {
    public EquipmentSet EquipmentSet { get; }
    public string Name { get; }
    public int TierCount { get; }
    public int TotalSpellCount { get; }

    public SpellSetListItem(EquipmentSet eqSet, DatReaderWriter.Types.SpellSet spellSet) {
        EquipmentSet = eqSet;
        Name = eqSet.ToString();
        TierCount = spellSet.SpellSetTiers.Count;
        TotalSpellCount = spellSet.SpellSetTiers.Values.Sum(t => t.Spells.Count);
    }

    public override string ToString() => $"{Name} ({TierCount} tiers, {TotalSpellCount} spells)";
}

public class SpellPickerItem {
    public uint Id { get; }
    public string Name { get; }
    public string IdHex { get; }
    public string DisplayLabel { get; }

    public SpellPickerItem(uint id, SpellBase spell) {
        Id = id;
        Name = spell.Name?.ToString() ?? $"Spell {id}";
        IdHex = $"0x{id:X4}";
        DisplayLabel = $"{IdHex} - {Name}";
    }

    public override string ToString() => DisplayLabel;
}

public partial class SpellSetDetailViewModel : ObservableObject {
    public EquipmentSet EquipmentSet { get; }
    private readonly Dictionary<uint, SpellBase> _spellLookup;

    [ObservableProperty] private ObservableCollection<TierViewModel> _tiers = new();

    public List<SpellPickerItem> AllSpells { get; }

    public SpellSetDetailViewModel(EquipmentSet eqSet, DatReaderWriter.Types.SpellSet spellSet,
        Dictionary<uint, SpellBase> spells) {
        EquipmentSet = eqSet;
        _spellLookup = spells;

        AllSpells = spells
            .OrderBy(kvp => kvp.Value.Name?.ToString() ?? "")
            .Select(kvp => new SpellPickerItem(kvp.Key, kvp.Value))
            .ToList();

        foreach (var kvp in spellSet.SpellSetTiers.OrderBy(k => k.Key)) {
            Tiers.Add(new TierViewModel(kvp.Key, kvp.Value, spells, AllSpells));
        }
    }

    public void AddNewTier() {
        uint nextKey = Tiers.Count > 0 ? Tiers.Max(t => t.TierKey) + 1 : 1;
        Tiers.Add(new TierViewModel(nextKey, new SpellSetTiers(), _spellLookup, AllSpells));
    }

    public void ApplyTo(DatReaderWriter.Types.SpellSet spellSet) {
        spellSet.SpellSetTiers.Clear();
        foreach (var tier in Tiers) {
            var setTier = new SpellSetTiers();
            tier.ApplyTo(setTier);
            spellSet.SpellSetTiers[tier.TierKey] = setTier;
        }
    }
}

public partial class TierSpellSlot : ObservableObject {
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpell))]
    private SpellPickerItem? _selectedSpell;

    public bool HasSpell => SelectedSpell != null;

    public TierSpellSlot() { }

    public TierSpellSlot(SpellPickerItem? spell) {
        _selectedSpell = spell;
    }
}

public partial class TierViewModel : ObservableObject {
    public uint TierKey { get; }
    public List<SpellPickerItem> AllSpells { get; }

    [ObservableProperty] private ObservableCollection<TierSpellSlot> _spellSlots = new();

    public string TierLabel => $"Tier {TierKey} ({SpellSlots.Count} spells)";

    public TierViewModel(uint tierKey, SpellSetTiers tier, Dictionary<uint, SpellBase> spells,
        List<SpellPickerItem> allSpells) {
        TierKey = tierKey;
        AllSpells = allSpells;

        foreach (var spellId in tier.Spells) {
            var match = allSpells.FirstOrDefault(s => s.Id == spellId);
            SpellSlots.Add(new TierSpellSlot(match));
        }
    }

    [RelayCommand]
    private void AddSpellSlot() {
        SpellSlots.Add(new TierSpellSlot());
        OnPropertyChanged(nameof(TierLabel));
    }

    [RelayCommand]
    private void RemoveSpellSlot(TierSpellSlot? slot) {
        if (slot == null) return;
        SpellSlots.Remove(slot);
        OnPropertyChanged(nameof(TierLabel));
    }

    public void ApplyTo(SpellSetTiers tier) {
        tier.Spells.Clear();
        foreach (var slot in SpellSlots) {
            if (slot.SelectedSpell != null)
                tier.Spells.Add(slot.SelectedSpell.Id);
        }
    }
}
