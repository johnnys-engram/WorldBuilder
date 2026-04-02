using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DbSetup = DatReaderWriter.DBObjs.Setup;
using DbFrame = DatReaderWriter.Types.Frame;
using DbPosition = DatReaderWriter.Types.Position;
using DbStartingArea = DatReaderWriter.Types.StartingArea;
using HeritageGroupCG = DatReaderWriter.Types.HeritageGroupCG;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using DbCharGen = DatReaderWriter.DBObjs.CharGen;

namespace WorldBuilder.Modules.CharGen;

public partial class CharGenEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private DbCharGen? _charGen;
    private bool _initialized;
    private const uint CharGenId = 0x0E000002;

    public WorldBuilderSettings Settings { get; }

    public CharGenEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager, IDatReaderWriter dats) {
        Settings = settings;
        _project = project;
        _documentManager = documentManager;
        _dats = dats;
    }

    [ObservableProperty] private string _statusText = "Loading CharGen…";
    [ObservableProperty] private ObservableCollection<HeritageListItem> _heritageGroups = new();
    [ObservableProperty] private HeritageListItem? _selectedHeritage;
    [ObservableProperty] private HeritageDetailViewModel? _selectedDetail;
    [ObservableProperty] private ObservableCollection<StartingAreaViewModel> _startingAreas = new();
    [ObservableProperty] private bool _isCharGenEditingEnabled;

    public async Task InitializeAsync(CancellationToken ct = default) {
        if (_initialized) return;

        if (_project.IsReadOnly) {
            StatusText = "Read-only project — viewing CharGen only; saving is disabled.";
            LoadCharGenReadOnly();
            IsCharGenEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsCharGenEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadCharGen();
        IsCharGenEditingEnabled = true;
        _initialized = true;
    }

    void LoadCharGenReadOnly() {
        if (!_dats.Portal.TryGet<DbCharGen>(CharGenId, out var datTable)) {
            StatusText = "Failed to load CharGen from DAT";
            return;
        }
        _charGen = datTable;
        RefreshHeritageList();
        RefreshStartingAreas();
        StatusText = $"Loaded CharGen (read-only): {_charGen.HeritageGroups.Count} heritages, {_charGen.StartingAreas.Count} starting areas";
    }

    void LoadCharGen() {
        if (_portalDoc != null && _portalDoc.TryGetEntry<DbCharGen>(CharGenId, out var docTable) && docTable != null) {
            _charGen = docTable;
        }
        else if (!_dats.Portal.TryGet<DbCharGen>(CharGenId, out var datTable)) {
            StatusText = "Failed to load CharGen from DAT";
            return;
        }
        else {
            _charGen = datTable;
        }

        RefreshHeritageList();
        RefreshStartingAreas();
        StatusText = $"Loaded CharGen: {_charGen.HeritageGroups.Count} heritages, {_charGen.StartingAreas.Count} starting areas";
    }

    async Task PersistPortalAsync(CancellationToken ct) {
        if (_portalRental == null) return;
        _portalRental.Document.Version++;
        var persist = await _documentManager.PersistDocumentAsync(_portalRental, null, ct);
        if (persist.IsFailure)
            StatusText = $"Save to project failed: {persist.Error.Message}";
    }

    void RefreshHeritageList() {
        if (_charGen == null) return;
        var items = _charGen.HeritageGroups
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new HeritageListItem(kvp.Key, kvp.Value))
            .ToList();
        HeritageGroups = new ObservableCollection<HeritageListItem>(items);
    }

    void RefreshStartingAreas() {
        if (_charGen == null) return;
        var areas = _charGen.StartingAreas
            .Select(a => new StartingAreaViewModel(a))
            .ToList();
        StartingAreas = new ObservableCollection<StartingAreaViewModel>(areas);
    }

    partial void OnSelectedHeritageChanged(HeritageListItem? value) {
        if (value != null && _charGen != null &&
            _charGen.HeritageGroups.TryGetValue(value.Id, out var group)) {
            try {
                SelectedDetail = new HeritageDetailViewModel(value.Id, group, _charGen.HeritageGroups, _dats);
            }
            catch (Exception ex) {
                SelectedDetail = null;
                StatusText = $"Error loading heritage: {ex.Message}";
            }
        }
        else {
            SelectedDetail = null;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct) {
        if (!IsCharGenEditingEnabled || _charGen == null || _portalDoc == null) return;

        if (SelectedDetail != null && _charGen.HeritageGroups.TryGetValue(SelectedDetail.HeritageId, out var group)) {
            SelectedDetail.ApplyTo(group);
        }

        foreach (var areaVm in StartingAreas) {
            areaVm.ApplyTo();
        }

        _portalDoc.SetEntry(CharGenId, _charGen);

        if (SelectedDetail != null) {
            int idx = -1;
            for (int i = 0; i < HeritageGroups.Count; i++) {
                if (HeritageGroups[i].Id == SelectedDetail.HeritageId) { idx = i; break; }
            }
            if (idx >= 0 && _charGen.HeritageGroups.TryGetValue(SelectedDetail.HeritageId, out var updated)) {
                HeritageGroups[idx] = new HeritageListItem(SelectedDetail.HeritageId, updated);
            }
        }

        await PersistPortalAsync(ct);
        StatusText = "Saved CharGen to project.";
    }

    [RelayCommand]
    private void AddHeritage() {
        if (!IsCharGenEditingEnabled || _charGen == null) return;

        uint nextId = 1;
        if (_charGen.HeritageGroups.Count > 0)
            nextId = _charGen.HeritageGroups.Keys.Max() + 1;

        var newGroup = new HeritageGroupCG {
            Name = "New Heritage",
            AttributeCredits = 330,
            SkillCredits = 52
        };

        _charGen.HeritageGroups[nextId] = newGroup;
        RefreshHeritageList();
        SelectedHeritage = HeritageGroups.FirstOrDefault(h => h.Id == nextId);
        StatusText = $"Added new heritage #{nextId}. Click Save to persist.";
    }

    [RelayCommand]
    private async Task RemoveHeritageAsync(CancellationToken ct) {
        if (!IsCharGenEditingEnabled || SelectedDetail == null || _charGen == null || _portalDoc == null) return;

        var id = SelectedDetail.HeritageId;
        if (!_charGen.HeritageGroups.Remove(id)) return;

        _portalDoc.SetEntry(CharGenId, _charGen);
        await PersistPortalAsync(ct);

        SelectedDetail = null;
        RefreshHeritageList();
        StatusText = $"Deleted heritage #{id}.";
    }

    [RelayCommand]
    private void AddStartingArea() {
        if (!IsCharGenEditingEnabled || _charGen == null) return;

        var area = new DbStartingArea { Name = "New Area" };
        _charGen.StartingAreas.Add(area);
        RefreshStartingAreas();
        StatusText = "Added new starting area. Click Save to persist.";
    }

    [RelayCommand]
    private async Task RemoveStartingAreaAsync(StartingAreaViewModel? areaVm) {
        if (!IsCharGenEditingEnabled || areaVm == null || _charGen == null || _portalDoc == null) return;

        var backing = areaVm.BackingArea;
        if (!_charGen.StartingAreas.Remove(backing)) return;

        _portalDoc.SetEntry(CharGenId, _charGen);
        await PersistPortalAsync(CancellationToken.None);

        RefreshStartingAreas();
        StatusText = "Removed starting area.";
    }
}

public class HeritageListItem {
    public uint Id { get; }
    public string Name { get; }
    public string IdHex { get; }
    public uint AttributeCredits { get; }
    public uint SkillCredits { get; }

    public HeritageListItem(uint id, HeritageGroupCG group) {
        Id = id;
        try {
            Name = group.Name?.ToString() ?? $"Heritage {id}";
            AttributeCredits = group.AttributeCredits;
            SkillCredits = group.SkillCredits;
        }
        catch {
            Name = $"Heritage {id}";
        }
        IdHex = $"0x{id:X2}";
    }

    public override string ToString() => $"{IdHex} - {Name}";
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

public partial class HeritageDetailViewModel : ObservableObject {
    public uint HeritageId { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private uint _iconId;
    [ObservableProperty] private WriteableBitmap? _iconBitmap;
    [ObservableProperty] private uint _attributeCredits;
    [ObservableProperty] private uint _skillCredits;
    [ObservableProperty] private uint _setupId;
    [ObservableProperty] private uint _environmentSetupId;

    [ObservableProperty] private string _setupInfo = "";
    [ObservableProperty] private string _envSetupInfo = "";

    [ObservableProperty] private ObservableCollection<IconPickerItem> _availableIcons = new();
    [ObservableProperty] private bool _isIconPickerOpen;

    private readonly IDatReaderWriter? _dats;

    partial void OnIconIdChanged(uint value) {
        if (_dats != null) {
            var localDats = _dats;
            _ = Task.Run(() => {
                var bmp = DatIconLoader.LoadIcon(localDats, value, 48);
                Dispatcher.UIThread.Post(() => IconBitmap = bmp);
            });
        }
    }

    partial void OnSetupIdChanged(uint value) => LoadSetupInfo(value, isEnv: false);

    partial void OnEnvironmentSetupIdChanged(uint value) => LoadSetupInfo(value, isEnv: true);

    public HeritageDetailViewModel(uint id, HeritageGroupCG group,
        Dictionary<uint, HeritageGroupCG> allGroups, IDatReaderWriter dats) {
        _dats = dats;
        HeritageId = id;
        try {
            Name = group.Name?.ToString() ?? "";
            IconId = group.IconId;
            AttributeCredits = group.AttributeCredits;
            SkillCredits = group.SkillCredits;
            SetupId = group.SetupId;
            EnvironmentSetupId = group.EnvironmentSetupId;
        }
        catch {
            Name = $"Heritage {id}";
        }

        try {
            LoadIconAsync(IconId);
            BuildAvailableIcons(allGroups);
        }
        catch { }
    }

    void LoadSetupInfo(uint id, bool isEnv) {
        if (id == 0) {
            if (isEnv) EnvSetupInfo = "Not set";
            else SetupInfo = "Not set";
            return;
        }

        var localDats = _dats;
        _ = Task.Run(() => {
            string info = BuildSetupInfoString(id, localDats);
            Dispatcher.UIThread.Post(() => {
                if (isEnv) EnvSetupInfo = info;
                else SetupInfo = info;
            });
        });
    }

    static string BuildSetupInfoString(uint id, IDatReaderWriter? dats) {
        bool isSetup = (id & 0xFF000000) == 0x02000000;
        if (dats == null) return $"0x{id:X8}";

        try {
            if (isSetup && dats.Portal.TryGet<DbSetup>(id, out var setup)) {
                int partCount = setup.Parts?.Count ?? 0;
                int placementCount = setup.PlacementFrames?.Count ?? 0;
                return $"Setup: {partCount} part(s), {placementCount} placement(s)";
            }
            else if (!isSetup) {
                return "GfxObj (single mesh)";
            }
        }
        catch { }
        return "Not found in DAT";
    }

    void LoadIconAsync(uint iconId) {
        if (iconId == 0 || _dats == null) return;
        var localDats = _dats;
        _ = Task.Run(() => {
            var bmp = DatIconLoader.LoadIcon(localDats, iconId, 48);
            Dispatcher.UIThread.Post(() => IconBitmap = bmp);
        });
    }

    void BuildAvailableIcons(Dictionary<uint, HeritageGroupCG> allGroups) {
        var snapshot = allGroups.Values.ToArray();
        var uniqueIconIds = snapshot
            .Select(g => g.IconId)
            .Where(id => id != 0)
            .Distinct()
            .ToList();
        uniqueIconIds.Sort();

        if (_dats != null) {
            var localDats = _dats;
            _ = Task.Run(() => {
                var items = new List<IconPickerItem>();
                foreach (var iid in uniqueIconIds) {
                    var item = new IconPickerItem(iid);
                    item.Bitmap = DatIconLoader.LoadIcon(localDats, iid, 32);
                    if (item.Bitmap != null)
                        items.Add(item);
                }
                Dispatcher.UIThread.Post(() => {
                    AvailableIcons = new ObservableCollection<IconPickerItem>(items);
                });
            });
        }
    }

    [RelayCommand]
    private void PickIcon(IconPickerItem? item) {
        if (item == null) return;
        IconId = item.Id;
        IsIconPickerOpen = false;
    }

    [RelayCommand]
    private void ToggleIconPicker() {
        IsIconPickerOpen = !IsIconPickerOpen;
    }

    public void ApplyTo(HeritageGroupCG group) {
        group.Name = Name;
        group.IconId = IconId;
        group.AttributeCredits = AttributeCredits;
        group.SkillCredits = SkillCredits;
        group.SetupId = SetupId;
        group.EnvironmentSetupId = EnvironmentSetupId;
    }
}

public partial class StartingAreaViewModel : ObservableObject {
    private readonly DbStartingArea _area;

    [ObservableProperty] private string _name;
    [ObservableProperty] private ObservableCollection<LocationViewModel> _locations = new();
    public int LocationCount => _area.Locations.Count;
    public DbStartingArea BackingArea => _area;

    public StartingAreaViewModel(DbStartingArea area) {
        _area = area;
        try { Name = area.Name?.ToString() ?? "(unnamed)"; }
        catch { Name = "(unnamed)"; }

        foreach (var loc in area.Locations) {
            Locations.Add(new LocationViewModel(loc));
        }
    }

    [RelayCommand]
    private void AddLocation() {
        var pos = new DbPosition {
            CellId = 0,
            Frame = new DbFrame {
                Origin = System.Numerics.Vector3.Zero,
                Orientation = System.Numerics.Quaternion.Identity
            }
        };
        _area.Locations.Add(pos);
        Locations.Add(new LocationViewModel(pos));
        OnPropertyChanged(nameof(LocationCount));
    }

    [RelayCommand]
    private void RemoveLocation(LocationViewModel? loc) {
        if (loc == null) return;
        _area.Locations.Remove(loc.BackingPosition);
        Locations.Remove(loc);
        OnPropertyChanged(nameof(LocationCount));
    }

    public void ApplyTo() {
        _area.Name = Name;
        foreach (var loc in Locations) {
            loc.ApplyTo();
        }
    }
}

public partial class LocationViewModel : ObservableObject {
    private readonly DbPosition _pos;

    [ObservableProperty] private string _cellId;
    [ObservableProperty] private string _x;
    [ObservableProperty] private string _y;
    [ObservableProperty] private string _z;

    public DbPosition BackingPosition => _pos;

    public LocationViewModel(DbPosition pos) {
        _pos = pos;
        CellId = $"0x{pos.CellId:X8}";
        X = pos.Frame.Origin.X.ToString("F2");
        Y = pos.Frame.Origin.Y.ToString("F2");
        Z = pos.Frame.Origin.Z.ToString("F2");
    }

    public void ApplyTo() {
        if (uint.TryParse(CellId.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var cid))
            _pos.CellId = cid;
        if (float.TryParse(X, out var fx)) _pos.Frame.Origin = new System.Numerics.Vector3(fx, _pos.Frame.Origin.Y, _pos.Frame.Origin.Z);
        if (float.TryParse(Y, out var fy)) _pos.Frame.Origin = new System.Numerics.Vector3(_pos.Frame.Origin.X, fy, _pos.Frame.Origin.Z);
        if (float.TryParse(Z, out var fz)) _pos.Frame.Origin = new System.Numerics.Vector3(_pos.Frame.Origin.X, _pos.Frame.Origin.Y, fz);
    }
}
