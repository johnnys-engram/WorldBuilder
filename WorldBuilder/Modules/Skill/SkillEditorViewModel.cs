using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.FrameworkDialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Skill;

public partial class SkillEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private readonly IDialogService _dialogService;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private SkillTable? _skillTable;
    private Dictionary<SkillId, SkillBase>? _allSkills;
    private bool _initialized;
    private const uint SkillTableId = 0x0E000004;

    [ObservableProperty] private string _statusText = "Loading skill editor…";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SkillCategory? _filterCategory;
    [ObservableProperty] private ObservableCollection<SkillListItem> _skills = new();
    [ObservableProperty] private SkillListItem? _selectedSkill;
    [ObservableProperty] private SkillDetailViewModel? _selectedDetail;
    [ObservableProperty] private int _totalSkillCount;
    [ObservableProperty] private int _filteredSkillCount;
    [ObservableProperty] private bool _isSkillEditingEnabled;

    public IReadOnlyList<SkillCategory?> CategoryOptions { get; } = new List<SkillCategory?> {
        null, SkillCategory.Combat, SkillCategory.Magic, SkillCategory.Other, SkillCategory.Undefined,
    };

    public WorldBuilderSettings Settings { get; }

    public SkillEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
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
            StatusText = "Read-only project — viewing skills only; saving is disabled.";
            LoadSkillsReadOnlyFromDat();
            IsSkillEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsSkillEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadSkills();
        IsSkillEditingEnabled = true;
        _initialized = true;
    }

    private void LoadSkillsReadOnlyFromDat() {
        if (!_dats.Portal.TryGet<SkillTable>(SkillTableId, out var datTable)) {
            StatusText = "Failed to load SkillTable from DAT";
            return;
        }

        _skillTable = datTable;
        _allSkills = _skillTable.Skills;
        TotalSkillCount = _allSkills.Count;
        ApplyFilter();
        StatusText = $"Loaded {TotalSkillCount} skills (read-only)";
    }

    private void LoadSkills() {
        if (_portalDoc != null && _portalDoc.TryGetEntry<SkillTable>(SkillTableId, out var docTable) && docTable != null) {
            _skillTable = docTable;
        }
        else if (!_dats.Portal.TryGet<SkillTable>(SkillTableId, out var datTable)) {
            StatusText = "Failed to load SkillTable from DAT";
            return;
        }
        else {
            _skillTable = datTable;
        }

        _allSkills = _skillTable.Skills;
        TotalSkillCount = _allSkills.Count;
        ApplyFilter();
        StatusText = $"Loaded {TotalSkillCount} skills";
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
    partial void OnFilterCategoryChanged(SkillCategory? value) => ApplyFilter();

    partial void OnSelectedSkillChanged(SkillListItem? value) {
        if (value != null && _allSkills != null && _allSkills.TryGetValue(value.Id, out var skill)) {
            try {
                SelectedDetail = new SkillDetailViewModel(value.Id, skill, _allSkills, _dats);
            }
            catch (Exception ex) {
                SelectedDetail = null;
                StatusText = $"Error loading skill: {ex.Message}";
            }
        }
        else {
            SelectedDetail = null;
        }
    }

    private void ApplyFilter() {
        if (_allSkills == null) return;

        var query = SearchText?.Trim() ?? "";

        var filtered = _allSkills
            .Where(kvp => {
                if (!string.IsNullOrEmpty(query) &&
                    !(kvp.Value.Name?.ToString() ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (FilterCategory.HasValue && kvp.Value.Category != FilterCategory.Value) return false;
                return true;
            })
            .OrderBy(kvp => (int)kvp.Key)
            .Select(kvp => new SkillListItem(kvp.Key, kvp.Value))
            .ToList();

        Skills = new ObservableCollection<SkillListItem>(filtered);
        FilteredSkillCount = filtered.Count;
    }

    [RelayCommand]
    private void ClearFilters() {
        SearchText = "";
        FilterCategory = null;
    }

    [RelayCommand]
    private void AddSkill() {
        if (!IsSkillEditingEnabled || _skillTable == null || _allSkills == null) return;

        int nextId = 1;
        if (_allSkills.Count > 0)
            nextId = _allSkills.Keys.Max(k => (int)k) + 1;

        var newSkill = new SkillBase {
            Name = $"New Skill {nextId}",
            Description = "",
            Category = SkillCategory.Other,
            Formula = new SkillFormula()
        };

        var skillId = (SkillId)nextId;
        _allSkills[skillId] = newSkill;
        TotalSkillCount = _allSkills.Count;
        ApplyFilter();

        SelectedSkill = Skills.FirstOrDefault(s => s.Id == skillId);
        StatusText = $"Added new skill {skillId}. Click Save Skill to persist.";
    }

    [RelayCommand]
    private async Task DeleteSkillAsync(CancellationToken ct) {
        if (!IsSkillEditingEnabled || SelectedDetail == null || _skillTable == null || _portalDoc == null || _allSkills == null) return;

        var id = SelectedDetail.SkillId;
        if (!_allSkills.Remove(id)) return;

        _portalDoc.SetEntry(SkillTableId, _skillTable);
        await PersistPortalAsync(ct);

        SelectedDetail = null;
        TotalSkillCount = _allSkills.Count;
        ApplyFilter();
        StatusText = $"Deleted skill {id}. Use File → Export Dats to write client_portal.dat.";
    }

    [RelayCommand]
    private async Task SaveSkillAsync(CancellationToken ct) {
        if (!IsSkillEditingEnabled || SelectedDetail == null || _skillTable == null || _portalDoc == null || _allSkills == null) return;

        var detail = SelectedDetail;
        var id = detail.SkillId;

        if (!_allSkills.TryGetValue(id, out var skill)) return;

        detail.ApplyTo(skill);

        _portalDoc.SetEntry(SkillTableId, _skillTable);
        await PersistPortalAsync(ct);

        int idx = -1;
        for (int i = 0; i < Skills.Count; i++) {
            if (Skills[i].Id == id) { idx = i; break; }
        }
        if (idx >= 0) {
            Skills[idx] = new SkillListItem(id, skill);
        }

        StatusText = $"Saved skill {skill.Name} to project. Use File → Export Dats for client_portal.dat.";
    }

    private string GetSuggestedDocumentsDirectory() =>
        string.IsNullOrEmpty(Settings.App.ProjectsDirectory)
            ? global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyDocuments)
            : Settings.App.ProjectsDirectory;

    private string GetSkillImportSuggestedDirectory() {
        var last = Settings.App.LastSkillTableImportDirectory;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last)) return last;
        return GetSuggestedDocumentsDirectory();
    }

    private void RememberSkillImportDirectory(string filePath) {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Settings.App.LastSkillTableImportDirectory = dir;
    }

    private async Task ApplyImportedSkillTableAsync(SkillTableExportFile doc, string path, CancellationToken ct) {
        doc.Skills ??= new List<SkillExportDto>();
        var ids = doc.Skills.Select(s => s.Id).ToList();
        if (ids.Count != ids.Distinct().Count()) {
            await _dialogService.ShowMessageBoxAsync(null, "Duplicate skill IDs in the import file.", "Import failed");
            return;
        }

        SkillTableImportExport.ReplaceSkillTable(_skillTable!, doc);
        _portalDoc!.SetEntry(SkillTableId, _skillTable!);
        await PersistPortalAsync(ct);

        SelectedSkill = null;
        SelectedDetail = null;
        TotalSkillCount = _allSkills!.Count;
        ApplyFilter();
        StatusText = $"Imported {_allSkills.Count} skills from {Path.GetFileName(path)}. Use File → Export Dats for client_portal.dat.";
    }

    [RelayCommand]
    private async Task ExportSkillsCsvAsync(CancellationToken ct) {
        if (_allSkills == null) return;

        var suggestedDir = GetSuggestedDocumentsDirectory();

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Export skill table (CSV)",
            DefaultExtension = "csv",
            SuggestedFileName = "skill-table.csv",
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeChoices = new[] {
                new FilePickerFileType("Skill table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Export failed");
            return;
        }

        try {
            await File.WriteAllTextAsync(path, SkillTableCsvSerializer.Serialize(_allSkills), ct);
            StatusText = $"Exported {_allSkills.Count} skills to {Path.GetFileName(path)} (CSV).";
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Export failed");
        }
    }

    [RelayCommand]
    private async Task ImportSkillsCsvAsync(CancellationToken ct) {
        if (!IsSkillEditingEnabled || _skillTable == null || _portalDoc == null || _allSkills == null) return;

        var suggestedDir = GetSkillImportSuggestedDirectory();

        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Import skill table — CSV (replaces all skills)",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeFilter = new[] {
                new FilePickerFileType("Skill table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Import failed");
            return;
        }

        RememberSkillImportDirectory(path);

        var confirm = await _dialogService.ShowMessageBoxAsync(null,
            "This will remove every skill in the current project skill table and replace it with the CSV file contents.\n\n"
            + "The first row must name all required skill columns (order does not matter).\n\n"
            + "This cannot be undone except by reverting project data.\n\nContinue?",
            "Replace entire skill table?",
            MessageBoxButton.YesNo);

        if (confirm != true) return;

        try {
            var csv = await File.ReadAllTextAsync(path, ct);
            var doc = SkillTableCsvSerializer.Parse(csv);
            await ApplyImportedSkillTableAsync(doc, path, ct);
        }
        catch (FormatException ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
    }
}

public class SkillListItem {
    public SkillId Id { get; }
    public string Name { get; }
    public string IdHex { get; }
    public SkillCategory Category { get; }
    public int TrainedCost { get; }
    public int SpecializedCost { get; }

    public SkillListItem(SkillId id, SkillBase skill) {
        Id = id;
        Name = skill.Name?.ToString() ?? "";
        IdHex = $"0x{(int)id:X2}";
        Category = skill.Category;
        TrainedCost = skill.TrainedCost;
        SpecializedCost = skill.SpecializedCost;
    }

    public override string ToString() => $"{IdHex} - {Name}";
}

public partial class IconPickerItem : ObservableObject {
    public uint Id { get; }
    public string IdHex { get; }
    /// <summary>How many skills in the loaded table use this <see cref="Id"/> (same idea as legacy iconSharedBySkillCount).</summary>
    public int SharedBySkillCount { get; }
    public string IconToolTip { get; }
    /// <summary>Skill picker: avoids fragile <c>#DetailPanel</c> / ancestor bindings in the item template.</summary>
    public ICommand? PickIconFromParentCommand { get; init; }
    [ObservableProperty] private WriteableBitmap? _bitmap;

    public IconPickerItem(uint id, IReadOnlyList<string> usedBySkillNames) {
        Id = id;
        IdHex = $"0x{id:X8}";
        SharedBySkillCount = Math.Max(1, usedBySkillNames.Count);
        IconToolTip = BuildIconToolTip(IdHex, usedBySkillNames);
    }

    private static string BuildIconToolTip(string idHex, IReadOnlyList<string> names) {
        if (names.Count == 0)
            return idHex;
        if (names.Count == 1)
            return $"{idHex} · {names[0]}";
        const int maxList = 5;
        var head = string.Join(", ", names.Take(maxList));
        var tail = names.Count > maxList ? $" (+{names.Count - maxList} more)" : "";
        return $"{idHex} · {names.Count} skills: {head}{tail}";
    }
}

public partial class SkillDetailViewModel : ObservableObject {
    public SkillId SkillId { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private uint _iconId;
    [ObservableProperty] private WriteableBitmap? _iconBitmap;
    [ObservableProperty] private int _trainedCost;
    [ObservableProperty] private int _specializedCost;
    [ObservableProperty] private SkillCategory _category;
    [ObservableProperty] private bool _chargenUse;
    [ObservableProperty] private uint _minLevel;
    [ObservableProperty] private double _upperBound;
    [ObservableProperty] private double _lowerBound;
    [ObservableProperty] private double _learnMod;

    [ObservableProperty] private int _formulaDivisor;
    [ObservableProperty] private AttributeId _formulaAttribute1;
    [ObservableProperty] private AttributeId? _formulaAttribute2;
    [ObservableProperty] private bool _formulaUseFormula;
    [ObservableProperty] private bool _formulaHasSecondAttribute;
    [ObservableProperty] private int _formulaUnknown;

    [ObservableProperty] private ObservableCollection<IconPickerItem> _availableIcons = new();
    [ObservableProperty] private bool _isIconPickerOpen;
    /// <summary>Explains distinct icon IDs vs skill count (shared IDs and 0x00000000).</summary>
    [ObservableProperty] private string _iconPickerSummary = "";

    public IReadOnlyList<SkillCategory> AllCategories { get; } = Enum.GetValues<SkillCategory>();
    public IReadOnlyList<AttributeId> AllAttributes { get; } = Enum.GetValues<AttributeId>();

    /// <summary>Attribute 2 combo: null = unused / raw 0 (not a named <see cref="AttributeId"/>).</summary>
    public IReadOnlyList<AttributeId?> AllAttributesWithNone { get; } =
        new List<AttributeId?> { null }.Concat(Enum.GetValues<AttributeId>().Select(a => (AttributeId?)a)).ToArray();

    private readonly IDatReaderWriter? _dats;

    partial void OnFormulaHasSecondAttributeChanged(bool value) {
        if (!value) {
            FormulaAttribute2 = null;
            return;
        }
        if (FormulaAttribute2.HasValue) return;
        var attrs = Enum.GetValues<AttributeId>();
        if (attrs.Length > 0)
            FormulaAttribute2 = attrs[0];
    }

    partial void OnIconIdChanged(uint value) {
        if (_dats != null) {
            var localDats = _dats;
            Task.Run(() => {
                var bmp = DatIconLoader.LoadIcon(localDats, value, 48);
                Dispatcher.UIThread.Post(() => IconBitmap = bmp);
            });
        }
    }

    public SkillDetailViewModel(SkillId id, SkillBase skill, Dictionary<SkillId, SkillBase> allSkills, IDatReaderWriter dats) {
        _dats = dats;
        SkillId = id;
        Name = skill.Name?.ToString() ?? "";
        Description = skill.Description?.ToString() ?? "";
        IconId = skill.IconId;
        TrainedCost = skill.TrainedCost;
        SpecializedCost = skill.SpecializedCost;
        Category = skill.Category;
        ChargenUse = skill.ChargenUse;
        MinLevel = skill.MinLevel;
        UpperBound = skill.UpperBound;
        LowerBound = skill.LowerBound;
        LearnMod = skill.LearnMod;

        if (skill.Formula != null) {
            var a1 = skill.Formula.Attribute1;
            var a2 = skill.Formula.Attribute2;
            FormulaDivisor = skill.Formula.Divisor;
            FormulaAttribute1 = a1;
            FormulaUseFormula = skill.Formula.Attribute1Multiplier > 0;
            var useSecondAttr = skill.Formula.Attribute2Multiplier > 0;
            FormulaAttribute2 = useSecondAttr
                ? (Enum.IsDefined(typeof(AttributeId), a2) ? a2 : null)
                : null;
            FormulaHasSecondAttribute = useSecondAttr;
            FormulaUnknown = skill.Formula.AdditiveBonus;
        }

        try {
            LoadIconAsync(skill.IconId);
            BuildAvailableIcons(allSkills);
        }
        catch {
            // Icon load is best-effort
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

    private void BuildAvailableIcons(Dictionary<SkillId, SkillBase> allSkills) {
        // One tile per distinct icon ID (including 0x00000000). Tooltip lists every skill using that ID.
        var usageByIconUint = allSkills
            .GroupBy(kvp => (uint)kvp.Value.IconId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => (int)x.Key).Select(x => x.Value.Name?.ToString() ?? $"Skill 0x{(int)x.Key:X}").ToList());

        var uniqueIconIds = usageByIconUint.Keys.ToList();
        if (!uniqueIconIds.Contains(IconId))
            uniqueIconIds.Add(IconId);
        uniqueIconIds.Sort();

        var totalSkills = allSkills.Count;
        var zeroIconSkills = allSkills.Count(kvp => (uint)kvp.Value.IconId == 0);
        var summary =
            $"{uniqueIconIds.Count} distinct icon ID(s) · {totalSkills} skills total. " +
            $"{zeroIconSkills} use 0x00000000; multiple skills can share one icon (hover a tile for names).";

        if (_dats != null) {
            var localDats = _dats;
            var pickIconCmd = PickIconCommand;
            Task.Run(() => {
                var items = new List<IconPickerItem>();
                foreach (var iconId in uniqueIconIds) {
                    usageByIconUint.TryGetValue(iconId, out var names);
                    names ??= new List<string>();
                    var item = new IconPickerItem(iconId, names) { PickIconFromParentCommand = pickIconCmd };
                    item.Bitmap = DatIconLoader.LoadIcon(localDats, iconId, 32);
                    items.Add(item);
                }
                Dispatcher.UIThread.Post(() => {
                    AvailableIcons = new ObservableCollection<IconPickerItem>(items);
                    IconPickerSummary = summary;
                });
            });
        }
        else {
            IconPickerSummary = summary;
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

    public void ApplyTo(SkillBase skill) {
        skill.Name = Name;
        skill.Description = Description;
        skill.IconId = IconId;
        skill.TrainedCost = TrainedCost;
        skill.SpecializedCost = SpecializedCost;
        skill.Category = Category;
        skill.ChargenUse = ChargenUse;
        skill.MinLevel = MinLevel;
        skill.UpperBound = UpperBound;
        skill.LowerBound = LowerBound;
        skill.LearnMod = LearnMod;

        skill.Formula ??= new SkillFormula();
        skill.Formula.Divisor = FormulaDivisor;
        skill.Formula.Attribute1 = FormulaAttribute1;
        AttributeId attr2Stored = default;
        if (FormulaHasSecondAttribute && FormulaAttribute2.HasValue)
            attr2Stored = FormulaAttribute2.Value;
        skill.Formula.Attribute2 = attr2Stored;
        skill.Formula.Attribute1Multiplier = FormulaUseFormula ? 1 : 0;
        skill.Formula.Attribute2Multiplier = FormulaHasSecondAttribute ? 1 : 0;
        skill.Formula.AdditiveBonus = FormulaUnknown;
    }
}
