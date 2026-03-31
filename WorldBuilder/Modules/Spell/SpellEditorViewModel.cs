using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.FrameworkDialogs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using SpellItemType = DatReaderWriter.Enums.ItemType;
using SpellRow = DatReaderWriter.Types.SpellBase;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Spell;

public partial class SpellEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private readonly IDialogService _dialogService;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private SpellTable? _spellTable;
    private Dictionary<uint, SpellRow>? _allSpells;
    private SpellComponentTable? _componentTable;
    private bool _initialized;
    private const uint SpellTableId = 0x0E00000E;

    [ObservableProperty] private string _statusText = "Loading spell editor…";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private MagicSchool? _filterSchool;
    [ObservableProperty] private SpellType? _filterSpellType;
    [ObservableProperty] private ObservableCollection<SpellListItem> _spells = new();
    [ObservableProperty] private SpellListItem? _selectedSpell;
    [ObservableProperty] private SpellDetailViewModel? _selectedDetail;
    [ObservableProperty] private int _totalSpellCount;
    [ObservableProperty] private int _filteredSpellCount;
    [ObservableProperty] private bool _isSpellEditingEnabled;

    public IReadOnlyList<MagicSchool?> SchoolOptions { get; } = new List<MagicSchool?> {
        null, MagicSchool.WarMagic, MagicSchool.LifeMagic,
        MagicSchool.ItemEnchantment, MagicSchool.CreatureEnchantment, MagicSchool.VoidMagic,
    };

    public IReadOnlyList<SpellType?> SpellTypeOptions { get; } = new List<SpellType?> {
        null, SpellType.Enchantment, SpellType.Projectile, SpellType.Boost,
        SpellType.Transfer, SpellType.PortalLink, SpellType.PortalRecall,
        SpellType.PortalSummon, SpellType.PortalSending, SpellType.Dispel,
        SpellType.LifeProjectile, SpellType.FellowBoost, SpellType.FellowEnchantment,
        SpellType.FellowPortalSending, SpellType.FellowDispel, SpellType.EnchantmentProjectile,
    };

    public WorldBuilderSettings Settings { get; }

    public SpellEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
        IDatReaderWriter dats, IDialogService dialogService) {
        Settings = settings;
        _project = project;
        _documentManager = documentManager;
        _dats = dats;
        _dialogService = dialogService;
    }

    public async Task InitializeAsync(CancellationToken ct = default) {
        if (_initialized) return;

        if (_project.IsReadOnly) {
            StatusText = "Read-only project — viewing spells only; saving is disabled.";
            LoadSpellsReadOnlyFromDat();
            IsSpellEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsSpellEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadSpells();
        IsSpellEditingEnabled = true;
        _initialized = true;
    }

    private void LoadSpellsReadOnlyFromDat() {
        if (!_dats.Portal.TryGet<SpellTable>(SpellTableId, out var datTable)) {
            StatusText = "Failed to load SpellTable from DAT";
            return;
        }

        _spellTable = datTable;
        _allSpells = _spellTable.Spells;
        TotalSpellCount = _allSpells.Count;
        _dats.Portal.TryGet<SpellComponentTable>(0x0E00000F, out var compTable);
        _componentTable = compTable;
        ApplyFilter();
        StatusText = $"Loaded {TotalSpellCount} spells (read-only), {_componentTable?.Components.Count ?? 0} components";
    }

    private void LoadSpells() {
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

        _allSpells = _spellTable.Spells;
        TotalSpellCount = _allSpells.Count;

        _dats.Portal.TryGet<SpellComponentTable>(0x0E00000F, out var compTable);
        _componentTable = compTable;

        ApplyFilter();
        StatusText = $"Loaded {TotalSpellCount} spells, {_componentTable?.Components.Count ?? 0} components";
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
    partial void OnFilterSchoolChanged(MagicSchool? value) => ApplyFilter();
    partial void OnFilterSpellTypeChanged(SpellType? value) => ApplyFilter();

    partial void OnSelectedSpellChanged(SpellListItem? value) {
        if (value != null && _allSpells != null && _allSpells.TryGetValue(value.Id, out var spell)) {
            SelectedDetail = new SpellDetailViewModel(value.Id, spell, _componentTable, _allSpells, _dats);
        }
        else {
            SelectedDetail = null;
        }
    }

    private void ApplyFilter() {
        if (_allSpells == null) return;

        var query = SearchText?.Trim() ?? "";
        uint searchId = 0;
        bool hasHexIdSearch = query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(query.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out searchId);
        bool hasDecimalIdSearch = !hasHexIdSearch && query.Length > 0 && query.All(char.IsDigit)
            && uint.TryParse(query, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out searchId);

        var filtered = _allSpells
            .Where(kvp => {
                if (hasHexIdSearch || hasDecimalIdSearch) return kvp.Key == searchId;
                if (!string.IsNullOrEmpty(query) &&
                    !(kvp.Value.Name?.ToString() ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (FilterSchool.HasValue && kvp.Value.School != FilterSchool.Value) return false;
                if (FilterSpellType.HasValue && kvp.Value.MetaSpellType != FilterSpellType.Value) return false;
                return true;
            })
            .OrderBy(kvp => kvp.Key)
            .Take(500)
            .Select(kvp => new SpellListItem(kvp.Key, kvp.Value))
            .ToList();

        Spells = new ObservableCollection<SpellListItem>(filtered);
        FilteredSpellCount = filtered.Count;
    }

    [RelayCommand]
    private void ClearFilters() {
        SearchText = "";
        FilterSchool = null;
        FilterSpellType = null;
    }

    [RelayCommand]
    private void AddSpell() {
        if (!IsSpellEditingEnabled || _spellTable == null || _allSpells == null) return;

        uint nextId = 1;
        if (_allSpells.Count > 0)
            nextId = _allSpells.Keys.Max() + 1;

        var newSpell = new SpellRow { Name = $"New Spell {nextId}", Description = "" };
        _allSpells[nextId] = newSpell;
        TotalSpellCount = _allSpells.Count;
        ApplyFilter();

        SelectedSpell = Spells.FirstOrDefault(s => s.Id == nextId);
        StatusText = $"Added new spell #{nextId}. Click Save Spell to persist.";
    }

    [RelayCommand]
    private async Task DeleteSpellAsync(CancellationToken ct) {
        if (!IsSpellEditingEnabled || SelectedDetail == null || _spellTable == null || _portalDoc == null || _allSpells == null) return;

        var id = SelectedDetail.SpellId;
        if (!_allSpells.Remove(id)) return;

        _portalDoc.SetEntry(SpellTableId, _spellTable);
        await PersistPortalAsync(ct);

        SelectedDetail = null;
        TotalSpellCount = _allSpells.Count;
        ApplyFilter();
        StatusText = $"Deleted spell #{id}. Use File → Export Dats to write client_portal.dat.";
    }

    [RelayCommand]
    private async Task SaveSpellAsync(CancellationToken ct) {
        if (!IsSpellEditingEnabled || SelectedDetail == null || _spellTable == null || _portalDoc == null || _allSpells == null) return;

        var detail = SelectedDetail;
        var id = detail.SpellId;

        if (!_allSpells.TryGetValue(id, out var spell)) return;

        detail.ApplyTo(spell);

        _portalDoc.SetEntry(SpellTableId, _spellTable);
        await PersistPortalAsync(ct);

        int idx = -1;
        for (int i = 0; i < Spells.Count; i++) {
            if (Spells[i].Id == id) { idx = i; break; }
        }
        if (idx >= 0) {
            Spells[idx] = new SpellListItem(id, spell);
        }

        StatusText = $"Saved spell #{id}: {spell.Name} to project. Use File → Export Dats for client_portal.dat.";
    }

    private string GetSuggestedDocumentsDirectory() =>
        string.IsNullOrEmpty(Settings.App.ProjectsDirectory)
            ? global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyDocuments)
            : Settings.App.ProjectsDirectory;

    private async Task ApplyImportedSpellTableAsync(SpellTableExportFile doc, string path, CancellationToken ct) {
        doc.Spells ??= new List<SpellExportDto>();
        var ids = doc.Spells.Select(s => s.Id).ToList();
        if (ids.Count != ids.Distinct().Count()) {
            await _dialogService.ShowMessageBoxAsync(null, "Duplicate spell IDs in the import file.", "Import failed");
            return;
        }

        SpellTableJsonSerializer.ReplaceSpellTable(_spellTable!, doc);
        _portalDoc!.SetEntry(SpellTableId, _spellTable!);
        await PersistPortalAsync(ct);

        SelectedSpell = null;
        TotalSpellCount = _allSpells!.Count;
        ApplyFilter();
        StatusText = $"Imported {_allSpells.Count} spells from {Path.GetFileName(path)}. Use File → Export Dats for client_portal.dat.";
    }

    [RelayCommand]
    private async Task ExportSpellsAsync(CancellationToken ct) {
        if (_allSpells == null) return;

        var suggestedDir = GetSuggestedDocumentsDirectory();

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Export spell table (JSON)",
            DefaultExtension = "json",
            SuggestedFileName = "spell-table.json",
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeChoices = new[] {
                new FilePickerFileType("Spell table JSON") { Patterns = new[] { "*.json" } },
            },
        });

        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Export failed");
            return;
        }

        try {
            var payload = SpellTableJsonSerializer.FromSpells(_allSpells);
            await File.WriteAllTextAsync(path, SpellTableJsonSerializer.Serialize(payload), ct);
            StatusText = $"Exported {_allSpells.Count} spells to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Export failed");
        }
    }

    [RelayCommand]
    private async Task ExportSpellsCsvAsync(CancellationToken ct) {
        if (_allSpells == null) return;

        var suggestedDir = GetSuggestedDocumentsDirectory();

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Export spell table (CSV)",
            DefaultExtension = "csv",
            SuggestedFileName = "spell-table.csv",
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeChoices = new[] {
                new FilePickerFileType("Spell table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Export failed");
            return;
        }

        try {
            await File.WriteAllTextAsync(path, SpellTableCsvSerializer.Serialize(_allSpells), ct);
            StatusText = $"Exported {_allSpells.Count} spells to {Path.GetFileName(path)} (CSV).";
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Export failed");
        }
    }

    [RelayCommand]
    private async Task ImportSpellsAsync(CancellationToken ct) {
        if (!IsSpellEditingEnabled || _spellTable == null || _portalDoc == null || _allSpells == null) return;

        var suggestedDir = GetSuggestedDocumentsDirectory();

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Import spell table — JSON (replaces all spells)",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeFilter = new[] {
                new FilePickerFileType("Spell table JSON") { Patterns = new[] { "*.json" } },
            },
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Import failed");
            return;
        }

        var confirm = await _dialogService.ShowMessageBoxAsync(null,
            "This will remove every spell in the current project spell table and replace it with the JSON file contents.\n\n"
            + "This cannot be undone except by reverting project data.\n\nContinue?",
            "Replace entire spell table?",
            MessageBoxButton.YesNo);

        if (confirm != true) return;

        try {
            var json = await File.ReadAllTextAsync(path, ct);
            var doc = SpellTableJsonSerializer.Deserialize(json);
            var version = doc.FormatVersion == 0 ? 1 : doc.FormatVersion;
            if (version < 1 || version > SpellTableImportExport.CurrentFormatVersion) {
                await _dialogService.ShowMessageBoxAsync(null,
                    $"Unsupported formatVersion {doc.FormatVersion} (expected 1–{SpellTableImportExport.CurrentFormatVersion}, or omit for 1).",
                    "Import failed");
                return;
            }

            await ApplyImportedSpellTableAsync(doc, path, ct);
        }
        catch (JsonException ex) {
            await _dialogService.ShowMessageBoxAsync(null, $"Invalid JSON: {ex.Message}", "Import failed");
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
    }

    [RelayCommand]
    private async Task ImportSpellsCsvAsync(CancellationToken ct) {
        if (!IsSpellEditingEnabled || _spellTable == null || _portalDoc == null || _allSpells == null) return;

        var suggestedDir = GetSuggestedDocumentsDirectory();

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Import spell table — CSV (replaces all spells)",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeFilter = new[] {
                new FilePickerFileType("Spell table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Import failed");
            return;
        }

        var confirm = await _dialogService.ShowMessageBoxAsync(null,
            "This will remove every spell in the current project spell table and replace it with the CSV file contents.\n\n"
            + "The first row must be the header row from a spell editor CSV export.\n\n"
            + "This cannot be undone except by reverting project data.\n\nContinue?",
            "Replace entire spell table?",
            MessageBoxButton.YesNo);

        if (confirm != true) return;

        try {
            var csv = await File.ReadAllTextAsync(path, ct);
            var doc = SpellTableCsvSerializer.Parse(csv);
            await ApplyImportedSpellTableAsync(doc, path, ct);
        }
        catch (FormatException ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
    }
}

public class SpellListItem {
    public uint Id { get; }
    public string Name { get; }
    public MagicSchool School { get; }
    public SpellType MetaSpellType { get; }
    public uint Power { get; }
    public uint BaseMana { get; }

    public SpellListItem(uint id, SpellRow spell) {
        Id = id;
        Name = spell.Name?.ToString() ?? "";
        School = spell.School;
        MetaSpellType = spell.MetaSpellType;
        Power = spell.Power;
        BaseMana = spell.BaseMana;
    }

    public override string ToString() => $"{Id} — {Name}";
}

public partial class ComponentPickerItem : ObservableObject {
    public uint Id { get; }
    public string Name { get; }
    public string TypeName { get; }
    public string DisplayLabel { get; }

    [ObservableProperty] private WriteableBitmap? _icon;

    public ComponentPickerItem(uint id, SpellComponentBase comp) {
        Id = id;
        Name = comp.Name?.ToString() ?? $"#{id}";
        TypeName = comp.Type.ToString();
        DisplayLabel = $"{Name} ({TypeName})";
    }

    public override string ToString() => DisplayLabel;
}

public partial class SpellComponentSlot : ObservableObject {
    public int SlotIndex { get; }
    public string SlotLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComponent))]
    private ComponentPickerItem? _selectedComponent;

    public bool HasComponent => SelectedComponent != null;

    public SpellComponentSlot(int slotIndex) {
        SlotIndex = slotIndex;
        SlotLabel = $"#{slotIndex + 1}";
    }
}

public partial class IconPickerItem : ObservableObject {
    public uint Id { get; }
    public string IdHex { get; }

    [ObservableProperty] private WriteableBitmap? _bitmap;

    public IconPickerItem(uint id) {
        Id = id;
        IdHex = $"0x{id:X8}";
    }
}

public partial class SpellDetailViewModel : ObservableObject {
    public uint SpellId { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private MagicSchool _school;
    [ObservableProperty] private SpellType _metaSpellType;
    [ObservableProperty] private SpellCategory _category;
    [ObservableProperty] private uint _icon;
    [ObservableProperty] private WriteableBitmap? _iconBitmap;
    [ObservableProperty] private uint _baseMana;
    [ObservableProperty] private uint _power;
    [ObservableProperty] private float _baseRangeConstant;
    [ObservableProperty] private float _baseRangeMod;
    [ObservableProperty] private float _spellEconomyMod;
    [ObservableProperty] private uint _formulaVersion;
    [ObservableProperty] private float _componentLoss;
    [ObservableProperty] private uint _bitfield;
    [ObservableProperty] private uint _metaSpellId;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private float _degradeModifier;
    [ObservableProperty] private float _degradeLimit;
    [ObservableProperty] private double _portalLifetime;
    [ObservableProperty] private PlayScript _casterEffect;
    [ObservableProperty] private PlayScript _targetEffect;
    [ObservableProperty] private PlayScript _fizzleEffect;
    [ObservableProperty] private double _recoveryInterval;
    [ObservableProperty] private float _recoveryAmount;
    [ObservableProperty] private uint _displayOrder;
    [ObservableProperty] private uint _nonComponentTargetType;
    [ObservableProperty] private uint _manaMod;

    [ObservableProperty] private ObservableCollection<SpellComponentSlot> _componentSlots = new();
    [ObservableProperty] private ObservableCollection<IconPickerItem> _availableIcons = new();
    [ObservableProperty] private bool _isIconPickerOpen;

    public List<ComponentPickerItem> AllComponents { get; private set; } = new();

    public bool IsEnchantment => MetaSpellType == SpellType.Enchantment || MetaSpellType == SpellType.FellowEnchantment;
    public bool IsPortalSummon => MetaSpellType == SpellType.PortalSummon;
    public bool CanAddComponent => ComponentSlots.Count < 8;

    partial void OnMetaSpellTypeChanged(SpellType value) {
        OnPropertyChanged(nameof(IsEnchantment));
        OnPropertyChanged(nameof(IsPortalSummon));
    }

    partial void OnIconChanged(uint value) {
        if (_dats != null) {
            var localDats = _dats;
            Task.Run(() => {
                var bmp = DatIconLoader.LoadIcon(localDats, value, 48);
                Dispatcher.UIThread.Post(() => IconBitmap = bmp);
            });
        }
    }

    public IReadOnlyList<MagicSchool> AllSchools { get; } = Enum.GetValues<MagicSchool>();
    public IReadOnlyList<SpellType> AllSpellTypes { get; } = Enum.GetValues<SpellType>();
    public IReadOnlyList<PlayScript> AllPlayScripts { get; } = Enum.GetValues<PlayScript>();

    public ObservableCollection<FlagItem> BitfieldFlags { get; } = new();
    public ObservableCollection<FlagItem> TargetTypeFlags { get; } = new();

    public string BitfieldDisplay => BitfieldFlags.Any(f => f.IsChecked)
        ? string.Join(", ", BitfieldFlags.Where(f => f.IsChecked).Select(f => f.Name))
        : "(none)";

    public string TargetTypeDisplay => TargetTypeFlags.Any(f => f.IsChecked)
        ? string.Join(", ", TargetTypeFlags.Where(f => f.IsChecked).Select(f => f.Name))
        : "(none)";

    private readonly SpellComponentTable? _componentTable;
    private readonly IDatReaderWriter? _dats;
    private uint _extraBitfieldBits;
    private uint _extraTargetTypeBits;

    public SpellDetailViewModel(uint id, SpellRow spell, SpellComponentTable? componentTable,
        Dictionary<uint, SpellRow> allSpells, IDatReaderWriter dats) {
        _componentTable = componentTable;
        _dats = dats;

        SpellId = id;
        Name = spell.Name?.ToString() ?? "";
        Description = spell.Description?.ToString() ?? "";
        School = spell.School;
        MetaSpellType = spell.MetaSpellType;
        Category = spell.Category;
        Icon = spell.Icon;
        BaseMana = spell.BaseMana;
        Power = spell.Power;
        BaseRangeConstant = spell.BaseRangeConstant;
        BaseRangeMod = spell.BaseRangeMod;
        SpellEconomyMod = spell.SpellEconomyMod;
        FormulaVersion = spell.FormulaVersion;
        ComponentLoss = spell.ComponentLoss;
        Bitfield = (uint)spell.Bitfield;
        MetaSpellId = spell.MetaSpellId;
        Duration = spell.Duration;
        DegradeModifier = spell.DegradeModifier;
        DegradeLimit = spell.DegradeLimit;
        PortalLifetime = spell.PortalLifetime;
        CasterEffect = spell.CasterEffect;
        TargetEffect = spell.TargetEffect;
        FizzleEffect = spell.FizzleEffect;
        RecoveryInterval = spell.RecoveryInterval;
        RecoveryAmount = spell.RecoveryAmount;
        DisplayOrder = spell.DisplayOrder;
        NonComponentTargetType = (uint)spell.NonComponentTargetType;
        ManaMod = spell.ManaMod;

        InitBitfieldFlags(Bitfield);
        InitTargetTypeFlags(NonComponentTargetType);

        BuildAllComponents();
        BuildComponentSlots(spell.Components);
        BuildAvailableIcons(allSpells);
        LoadIconAsync(spell.Icon);
    }

    private void BuildAllComponents() {
        if (_componentTable == null) return;
        AllComponents = _componentTable.Components
            .OrderBy(kvp => kvp.Value.Name?.ToString() ?? "")
            .Select(kvp => new ComponentPickerItem(kvp.Key, kvp.Value))
            .ToList();

        if (_dats != null) {
            var localDats = _dats;
            foreach (var item in AllComponents) {
                var comp = _componentTable.Components[item.Id];
                if (comp.Icon == 0) continue;
                var localItem = item;
                var localIconId = comp.Icon;
                Task.Run(() => {
                    var bmp = DatIconLoader.LoadIcon(localDats, localIconId, 20);
                    Dispatcher.UIThread.Post(() => localItem.Icon = bmp);
                });
            }
        }
    }

    private void BuildComponentSlots(List<uint> componentIds) {
        ComponentSlots.Clear();
        for (int i = 0; i < componentIds.Count; i++) {
            var slot = new SpellComponentSlot(i);
            var match = AllComponents.FirstOrDefault(c => c.Id == componentIds[i]);
            slot.SelectedComponent = match;
            ComponentSlots.Add(slot);
        }
        OnPropertyChanged(nameof(CanAddComponent));
    }

    private void BuildAvailableIcons(Dictionary<uint, SpellRow> allSpells) {
        var snapshot = allSpells.Values.ToArray();
        var uniqueIconIds = snapshot
            .Select(s => s.Icon)
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        uniqueIconIds.Sort();

        if (_dats != null) {
            var localDats = _dats;
            Task.Run(() => {
                var items = new List<IconPickerItem>();
                foreach (var iconId in uniqueIconIds) {
                    var item = new IconPickerItem(iconId);
                    item.Bitmap = DatIconLoader.LoadIcon(localDats, iconId, 32);
                    if (item.Bitmap != null)
                        items.Add(item);
                }
                Dispatcher.UIThread.Post(() => {
                    AvailableIcons = new ObservableCollection<IconPickerItem>(items);
                });
            });
        }
    }

    private void LoadIconAsync(uint iconId) {
        if (iconId == 0 || _dats == null) return;
        var localDats = _dats;
        Task.Run(() => {
            var bmp = DatIconLoader.LoadIcon(localDats, iconId, 48);
            Dispatcher.UIThread.Post(() => IconBitmap = bmp);
        });
    }

    [RelayCommand]
    private void PickIcon(IconPickerItem? item) {
        if (item == null) return;
        Icon = item.Id;
        IsIconPickerOpen = false;
    }

    [RelayCommand]
    private void ToggleIconPicker() {
        IsIconPickerOpen = !IsIconPickerOpen;
    }

    [RelayCommand]
    private void AddComponent() {
        if (ComponentSlots.Count >= 8) return;
        var slot = new SpellComponentSlot(ComponentSlots.Count);
        ComponentSlots.Add(slot);
        OnPropertyChanged(nameof(CanAddComponent));
    }

    [RelayCommand]
    private void RemoveComponent(SpellComponentSlot? slot) {
        if (slot == null) return;
        ComponentSlots.Remove(slot);
        OnPropertyChanged(nameof(CanAddComponent));
    }

    [RelayCommand]
    private void MoveComponentUp(SpellComponentSlot? slot) {
        if (slot == null) return;
        int idx = ComponentSlots.IndexOf(slot);
        if (idx <= 0) return;
        ComponentSlots.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveComponentDown(SpellComponentSlot? slot) {
        if (slot == null) return;
        int idx = ComponentSlots.IndexOf(slot);
        if (idx < 0 || idx >= ComponentSlots.Count - 1) return;
        ComponentSlots.Move(idx, idx + 1);
    }

    private void InitBitfieldFlags(uint bitfield) {
        uint knownBits = 0;
        foreach (var flag in Enum.GetValues<SpellIndex>()) {
            var val = (uint)flag;
            if (val == 0 || (val & (val - 1)) != 0) continue;
            knownBits |= val;
            var item = new FlagItem(flag.ToString(), val, (bitfield & val) != 0);
            item.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(FlagItem.IsChecked)) UpdateBitfieldFromFlags();
            };
            BitfieldFlags.Add(item);
        }
        _extraBitfieldBits = bitfield & ~knownBits;
    }

    private void UpdateBitfieldFromFlags() {
        uint val = _extraBitfieldBits;
        foreach (var f in BitfieldFlags)
            if (f.IsChecked) val |= f.Value;
        Bitfield = val;
        OnPropertyChanged(nameof(BitfieldDisplay));
    }

    private void InitTargetTypeFlags(uint targetType) {
        uint knownBits = 0;
        foreach (var flag in Enum.GetValues<SpellItemType>()) {
            var val = (uint)flag;
            if (val == 0 || (val & (val - 1)) != 0) continue;
            knownBits |= val;
            var item = new FlagItem(flag.ToString(), val, (targetType & val) != 0);
            item.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(FlagItem.IsChecked)) UpdateTargetTypeFromFlags();
            };
            TargetTypeFlags.Add(item);
        }
        _extraTargetTypeBits = targetType & ~knownBits;
    }

    private void UpdateTargetTypeFromFlags() {
        uint val = _extraTargetTypeBits;
        foreach (var f in TargetTypeFlags)
            if (f.IsChecked) val |= f.Value;
        NonComponentTargetType = val;
        OnPropertyChanged(nameof(TargetTypeDisplay));
    }

    public void ApplyTo(SpellRow spell) {
        spell.Name = Name;
        spell.Description = Description;
        spell.School = School;
        spell.MetaSpellType = MetaSpellType;
        spell.Category = Category;
        spell.Icon = Icon;
        spell.BaseMana = BaseMana;
        spell.Power = Power;
        spell.BaseRangeConstant = BaseRangeConstant;
        spell.BaseRangeMod = BaseRangeMod;
        spell.SpellEconomyMod = SpellEconomyMod;
        spell.FormulaVersion = FormulaVersion;
        spell.ComponentLoss = ComponentLoss;
        spell.Bitfield = (SpellIndex)Bitfield;
        spell.MetaSpellId = MetaSpellId;
        spell.Duration = Duration;
        spell.DegradeModifier = DegradeModifier;
        spell.DegradeLimit = DegradeLimit;
        spell.PortalLifetime = PortalLifetime;
        spell.CasterEffect = CasterEffect;
        spell.TargetEffect = TargetEffect;
        spell.FizzleEffect = FizzleEffect;
        spell.RecoveryInterval = RecoveryInterval;
        spell.RecoveryAmount = RecoveryAmount;
        spell.DisplayOrder = DisplayOrder;
        spell.NonComponentTargetType = (SpellItemType)NonComponentTargetType;
        spell.ManaMod = ManaMod;

        spell.Components = ComponentSlots
            .Where(s => s.SelectedComponent != null)
            .Select(s => s.SelectedComponent!.Id)
            .ToList();
    }
}

public partial class FlagItem : ObservableObject {
    public string Name { get; }
    public uint Value { get; }

    [ObservableProperty] private bool _isChecked;

    public FlagItem(string name, uint value, bool isChecked) {
        Name = name;
        Value = value;
        _isChecked = isChecked;
    }
}
