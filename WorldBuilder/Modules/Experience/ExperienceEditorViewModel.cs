using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.FrameworkDialogs;
using System.Collections.ObjectModel;
using System.IO;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Experience;

public partial class XpRow : ObservableObject {
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _value = "0";

    public XpRow(int index, string value) {
        _index = index;
        _value = value;
    }
}

public partial class LevelRow : ObservableObject {
    [ObservableProperty] private int _level;
    [ObservableProperty] private string _xpRequired = "0";
    [ObservableProperty] private string _skillCredits = "0";

    public LevelRow(int level, string xp, string credits) {
        _level = level;
        _xpRequired = xp;
        _skillCredits = credits;
    }
}

public partial class ExperienceEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private readonly IDialogService _dialogService;
    private DocumentRental<PortalDatDocument>? _portalRental;
    private PortalDatDocument? _portalDoc;
    private ExperienceTable? _table;
    private bool _initialized;
    private const uint ExperienceTableId = 0x0E000018;

    [ObservableProperty] private string _statusText = "Loading experience editor…";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isAutoScaleOpen;
    [ObservableProperty] private bool _isExperienceEditingEnabled;

    [ObservableProperty] private ObservableCollection<LevelRow> _levels = new();
    [ObservableProperty] private ObservableCollection<XpRow> _attributes = new();
    [ObservableProperty] private ObservableCollection<XpRow> _vitals = new();
    [ObservableProperty] private ObservableCollection<XpRow> _trainedSkills = new();
    [ObservableProperty] private ObservableCollection<XpRow> _specializedSkills = new();

    [ObservableProperty] private string _autoScaleTotalLevels = "275";
    [ObservableProperty] private string _autoScaleBaseXp = "1000";
    [ObservableProperty] private string _autoScaleGrowthRate = "2.5";
    [ObservableProperty] private string _autoScaleCreditsEveryN = "5";
    [ObservableProperty] private string _autoScaleAttributeRanks = "190";
    [ObservableProperty] private string _autoScaleVitalRanks = "196";
    [ObservableProperty] private string _autoScaleSkillRanks = "226";

    partial void OnAutoScaleTotalLevelsChanged(string value) {
        if (!int.TryParse(value, out int levels) || levels < 1) return;
        double scale = (double)levels / 275.0;
        AutoScaleAttributeRanks = Math.Max(10, (int)(190 * scale)).ToString();
        AutoScaleVitalRanks = Math.Max(10, (int)(196 * scale)).ToString();
        AutoScaleSkillRanks = Math.Max(10, (int)(226 * scale)).ToString();
    }

    public WorldBuilderSettings Settings { get; }

    public ExperienceEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
        IDatReaderWriter dats, IDialogService dialogService) {
        Settings = settings;
        _project = project;
        _documentManager = documentManager;
        _dats = dats;
        _dialogService = dialogService;
    }

    public bool CanExportExperienceSectionCsv => _table != null
        && ExperienceSectionCsvSerializer.TryGetSection(SelectedTabIndex, out _);

    public bool CanImportExperienceSectionCsv => IsExperienceEditingEnabled && CanExportExperienceSectionCsv;

    partial void OnSelectedTabIndexChanged(int value) {
        OnPropertyChanged(nameof(CanExportExperienceSectionCsv));
        OnPropertyChanged(nameof(CanImportExperienceSectionCsv));
    }

    partial void OnIsExperienceEditingEnabledChanged(bool value) => OnPropertyChanged(nameof(CanImportExperienceSectionCsv));

    public async Task InitializeAsync(CancellationToken ct = default) {
        if (_initialized) return;

        if (_project.IsReadOnly) {
            StatusText = "Read-only project — viewing experience table only; saving is disabled.";
            LoadTableReadOnly();
            IsExperienceEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<PortalDatDocument>(PortalDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open portal table document: {rentResult.Error.Message}";
            IsExperienceEditingEnabled = false;
            _initialized = true;
            return;
        }

        _portalRental = rentResult.Value;
        _portalDoc = _portalRental.Document;
        LoadTable();
        IsExperienceEditingEnabled = true;
        _initialized = true;
    }

    private void LoadTableReadOnly() {
        if (!_dats.Portal.TryGet<ExperienceTable>(ExperienceTableId, out var datTable)) {
            StatusText = "Failed to load ExperienceTable (0x0E000018) from DAT";
            return;
        }

        _table = datTable;
        PopulateCollections();
        OnPropertyChanged(nameof(CanExportExperienceSectionCsv));
        OnPropertyChanged(nameof(CanImportExperienceSectionCsv));
        StatusText = $"Loaded (read-only): {_table.Levels.Length} levels, {_table.Attributes.Length} attribute ranks, " +
                     $"{_table.Vitals.Length} vital ranks, {_table.TrainedSkills.Length} trained, " +
                     $"{_table.SpecializedSkills.Length} specialized";
    }

    private void LoadTable() {
        if (_portalDoc != null && _portalDoc.TryGetEntry<ExperienceTable>(ExperienceTableId, out var docTable) && docTable != null) {
            _table = docTable;
        }
        else if (!_dats.Portal.TryGet<ExperienceTable>(ExperienceTableId, out var datTable)) {
            StatusText = "Failed to load ExperienceTable (0x0E000018) from DAT";
            return;
        }
        else {
            _table = datTable;
        }

        PopulateCollections();
        OnPropertyChanged(nameof(CanExportExperienceSectionCsv));
        OnPropertyChanged(nameof(CanImportExperienceSectionCsv));
        StatusText = $"Loaded: {_table.Levels.Length} levels, {_table.Attributes.Length} attribute ranks, " +
                     $"{_table.Vitals.Length} vital ranks, {_table.TrainedSkills.Length} trained, " +
                     $"{_table.SpecializedSkills.Length} specialized";
    }

    private async Task PersistPortalAsync(CancellationToken ct) {
        if (_portalRental == null) return;
        _portalRental.Document.Version++;
        var persist = await _documentManager.PersistDocumentAsync(_portalRental, null, ct);
        if (persist.IsFailure) {
            StatusText = $"Save to project failed: {persist.Error.Message}";
        }
    }

    private void PopulateCollections() {
        if (_table == null) return;

        Levels.Clear();
        int levelCount = _table.Levels.Length;
        int creditCount = _table.SkillCredits.Length;
        for (int i = 0; i < levelCount; i++) {
            var credits = i < creditCount ? _table.SkillCredits[i].ToString() : "0";
            Levels.Add(new LevelRow(i, _table.Levels[i].ToString(), credits));
        }

        Attributes.Clear();
        for (int i = 0; i < _table.Attributes.Length; i++)
            Attributes.Add(new XpRow(i, _table.Attributes[i].ToString()));

        Vitals.Clear();
        for (int i = 0; i < _table.Vitals.Length; i++)
            Vitals.Add(new XpRow(i, _table.Vitals[i].ToString()));

        TrainedSkills.Clear();
        for (int i = 0; i < _table.TrainedSkills.Length; i++)
            TrainedSkills.Add(new XpRow(i, _table.TrainedSkills[i].ToString()));

        SpecializedSkills.Clear();
        for (int i = 0; i < _table.SpecializedSkills.Length; i++)
            SpecializedSkills.Add(new XpRow(i, _table.SpecializedSkills[i].ToString()));
    }

    [RelayCommand]
    private void AddLevel() {
        if (!IsExperienceEditingEnabled) return;
        int nextLevel = Levels.Count;
        Levels.Add(new LevelRow(nextLevel, "0", "0"));
        StatusText = $"Added level {nextLevel} (total: {Levels.Count})";
    }

    [RelayCommand]
    private void RemoveLevel() {
        if (!IsExperienceEditingEnabled) return;
        if (Levels.Count <= 1) {
            StatusText = "Cannot remove the last level";
            return;
        }
        int removed = Levels.Count - 1;
        Levels.RemoveAt(removed);
        StatusText = $"Removed level {removed} (total: {Levels.Count})";
    }

    [RelayCommand]
    private void AddRank() {
        if (!IsExperienceEditingEnabled) return;
        ObservableCollection<XpRow>? collection = GetActiveRankCollection();
        if (collection == null) {
            StatusText = "Select a rank tab (Attributes, Vitals, Trained, or Specialized)";
            return;
        }
        int nextIndex = collection.Count;
        collection.Add(new XpRow(nextIndex, "0"));
        StatusText = $"Added rank {nextIndex} to {GetActiveTabName()}";
    }

    [RelayCommand]
    private void RemoveRank() {
        if (!IsExperienceEditingEnabled) return;
        ObservableCollection<XpRow>? collection = GetActiveRankCollection();
        if (collection == null) {
            StatusText = "Select a rank tab (Attributes, Vitals, Trained, or Specialized)";
            return;
        }
        if (collection.Count <= 1) {
            StatusText = $"Cannot remove the last rank from {GetActiveTabName()}";
            return;
        }
        int removed = collection.Count - 1;
        collection.RemoveAt(removed);
        StatusText = $"Removed rank {removed} from {GetActiveTabName()}";
    }

    [RelayCommand]
    private void ToggleAutoScale() {
        IsAutoScaleOpen = !IsAutoScaleOpen;
    }

    private static ulong SafePow(double baseVal, double exp, int i) {
        if (i == 0) return 0;
        double val = baseVal * Math.Pow(i, exp);
        return val > (double)ulong.MaxValue ? ulong.MaxValue : (ulong)val;
    }

    private static uint SafePowUint(double baseVal, double exp, int i) {
        if (i == 0) return 0;
        double val = baseVal * Math.Pow(i, exp);
        return val > uint.MaxValue ? uint.MaxValue : (uint)val;
    }

    [RelayCommand]
    private void GenerateAutoScale() {
        if (!IsExperienceEditingEnabled) return;
        if (!int.TryParse(AutoScaleTotalLevels, out int totalLevels) || totalLevels < 1) {
            StatusText = "Invalid total levels"; return;
        }
        if (!double.TryParse(AutoScaleBaseXp, out double baseXp) || baseXp < 1) {
            StatusText = "Invalid base XP"; return;
        }
        if (!double.TryParse(AutoScaleGrowthRate, out double exponent) || exponent < 1.0) {
            StatusText = "Exponent must be >= 1.0"; return;
        }
        if (!int.TryParse(AutoScaleCreditsEveryN, out int creditsEveryN) || creditsEveryN < 1) {
            StatusText = "Credits-every-N must be >= 1"; return;
        }

        Levels.Clear();
        for (int i = 0; i < totalLevels; i++) {
            ulong xp = SafePow(baseXp, exponent, i);
            uint credits = (i > 0 && i % creditsEveryN == 0) ? 1u : 0u;
            Levels.Add(new LevelRow(i, xp.ToString(), credits.ToString()));
        }

        int attrRanks = 190, vitalRanks = 196, skillRanks = 226;
        int.TryParse(AutoScaleAttributeRanks, out attrRanks);
        int.TryParse(AutoScaleVitalRanks, out vitalRanks);
        int.TryParse(AutoScaleSkillRanks, out skillRanks);
        if (attrRanks < 1) attrRanks = 190;
        if (vitalRanks < 1) vitalRanks = 196;
        if (skillRanks < 1) skillRanks = 226;

        double attrBase = baseXp * 0.25;
        var newAttrs = new ObservableCollection<XpRow>();
        for (int i = 0; i < attrRanks; i++)
            newAttrs.Add(new XpRow(i, SafePowUint(attrBase, exponent, i).ToString()));
        Attributes = newAttrs;

        double vitalBase = baseXp * 0.2;
        var newVitals = new ObservableCollection<XpRow>();
        for (int i = 0; i < vitalRanks; i++)
            newVitals.Add(new XpRow(i, SafePowUint(vitalBase, exponent, i).ToString()));
        Vitals = newVitals;

        double trainedBase = baseXp * 0.33;
        var newTrained = new ObservableCollection<XpRow>();
        for (int i = 0; i < skillRanks; i++)
            newTrained.Add(new XpRow(i, SafePowUint(trainedBase, exponent, i).ToString()));
        TrainedSkills = newTrained;

        double specBase = baseXp * 0.2;
        var newSpec = new ObservableCollection<XpRow>();
        for (int i = 0; i < skillRanks; i++)
            newSpec.Add(new XpRow(i, SafePowUint(specBase, exponent, i).ToString()));
        SpecializedSkills = newSpec;

        StatusText = $"Generated all: {totalLevels} levels, {attrRanks} attr, {vitalRanks} vital, {skillRanks} skill ranks";
    }

    private ObservableCollection<XpRow>? GetActiveRankCollection() {
        return SelectedTabIndex switch {
            1 => Attributes,
            2 => Vitals,
            3 => TrainedSkills,
            4 => SpecializedSkills,
            _ => null
        };
    }

    private string GetActiveTabName() {
        return SelectedTabIndex switch {
            1 => "Attributes",
            2 => "Vitals",
            3 => "Trained Skills",
            4 => "Specialized Skills",
            _ => "Unknown"
        };
    }

    private void SyncUiToExperienceTable() {
        if (_table == null) return;

        _table.Levels = new ulong[Levels.Count];
        _table.SkillCredits = new uint[Levels.Count];
        for (int i = 0; i < Levels.Count; i++) {
            _table.Levels[i] = ulong.TryParse(Levels[i].XpRequired, out var xp) ? xp : 0;
            _table.SkillCredits[i] = uint.TryParse(Levels[i].SkillCredits, out var sc) ? sc : 0;
        }

        _table.Attributes = new uint[Attributes.Count];
        for (int i = 0; i < Attributes.Count; i++)
            _table.Attributes[i] = uint.TryParse(Attributes[i].Value, out var v) ? v : 0;

        _table.Vitals = new uint[Vitals.Count];
        for (int i = 0; i < Vitals.Count; i++)
            _table.Vitals[i] = uint.TryParse(Vitals[i].Value, out var v) ? v : 0;

        _table.TrainedSkills = new uint[TrainedSkills.Count];
        for (int i = 0; i < TrainedSkills.Count; i++)
            _table.TrainedSkills[i] = uint.TryParse(TrainedSkills[i].Value, out var v) ? v : 0;

        _table.SpecializedSkills = new uint[SpecializedSkills.Count];
        for (int i = 0; i < SpecializedSkills.Count; i++)
            _table.SpecializedSkills[i] = uint.TryParse(SpecializedSkills[i].Value, out var v) ? v : 0;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct) {
        if (!IsExperienceEditingEnabled || _table == null || _portalDoc == null) {
            StatusText = "Nothing to save";
            return;
        }

        try {
            SyncUiToExperienceTable();

            _portalDoc.SetEntry(ExperienceTableId, _table);
            await PersistPortalAsync(ct);
            StatusText = $"Saved: {Levels.Count} levels, {Attributes.Count} attr, " +
                         $"{Vitals.Count} vital, {TrainedSkills.Count} trained, " +
                         $"{SpecializedSkills.Count} specialized. Use File → Export Dats to write client_portal.dat.";
        }
        catch (Exception ex) {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    private string GetSuggestedDocumentsDirectory() =>
        string.IsNullOrEmpty(Settings.App.ProjectsDirectory)
            ? global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyDocuments)
            : Settings.App.ProjectsDirectory;

    private string GetExperienceImportSuggestedDirectory() {
        var last = Settings.App.LastExperienceTableImportDirectory;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last)) return last;
        return GetSuggestedDocumentsDirectory();
    }

    private void RememberExperienceImportDirectory(string filePath) {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Settings.App.LastExperienceTableImportDirectory = dir;
    }

    [RelayCommand]
    private async Task ExportExperienceSectionCsvAsync(CancellationToken ct) {
        if (_table == null || !ExperienceSectionCsvSerializer.TryGetSection(SelectedTabIndex, out var section))
            return;

        var suggestedDir = GetSuggestedDocumentsDirectory();
        var stem = ExperienceSectionCsvSerializer.SectionFileStem(section);

        var file = await TopLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = $"Export {ExperienceSectionCsvSerializer.SectionDisplayName(section)} (CSV)",
            DefaultExtension = "csv",
            SuggestedFileName = $"{stem}.csv",
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeChoices = new[] {
                new FilePickerFileType("Experience table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Export failed");
            return;
        }

        try {
            var text = section switch {
                ExperienceCsvSection.Levels => ExperienceSectionCsvSerializer.SerializeLevels(Levels),
                ExperienceCsvSection.Attributes => ExperienceSectionCsvSerializer.SerializeRankSection(Attributes),
                ExperienceCsvSection.Vitals => ExperienceSectionCsvSerializer.SerializeRankSection(Vitals),
                ExperienceCsvSection.TrainedSkills => ExperienceSectionCsvSerializer.SerializeRankSection(TrainedSkills),
                ExperienceCsvSection.SpecializedSkills => ExperienceSectionCsvSerializer.SerializeRankSection(SpecializedSkills),
                _ => throw new InvalidOperationException("Unknown section."),
            };
            await File.WriteAllTextAsync(path, text, ct);
            StatusText = $"Exported {ExperienceSectionCsvSerializer.SectionDisplayName(section)} to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Export failed");
        }
    }

    [RelayCommand]
    private async Task ImportExperienceSectionCsvAsync(CancellationToken ct) {
        if (!IsExperienceEditingEnabled || _table == null || _portalDoc == null) return;
        if (!ExperienceSectionCsvSerializer.TryGetSection(SelectedTabIndex, out var section)) return;

        var suggestedDir = GetExperienceImportSuggestedDirectory();
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = $"Import {ExperienceSectionCsvSerializer.SectionDisplayName(section)} — CSV (replaces this section only)",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedDir),
            FileTypeFilter = new[] {
                new FilePickerFileType("Experience table CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            await _dialogService.ShowMessageBoxAsync(null, "Could not resolve a local path for that file.", "Import failed");
            return;
        }

        RememberExperienceImportDirectory(path);

        var sectionLabel = ExperienceSectionCsvSerializer.SectionDisplayName(section);
        var confirm = await _dialogService.ShowMessageBoxAsync(null,
            $"This will replace only: {sectionLabel}\n\n"
            + "Other experience tabs are kept as they are in the editor (including unsaved edits). "
            + "The CSV index/level column must be contiguous starting at 0.\n\nContinue?",
            "Replace experience section?",
            MessageBoxButton.YesNo);

        if (confirm != true) return;

        try {
            var csv = await File.ReadAllTextAsync(path, ct);
            SyncUiToExperienceTable();

            switch (section) {
                case ExperienceCsvSection.Levels: {
                    var parsed = ExperienceSectionCsvSerializer.ParseLevels(csv);
                    _table.Levels = parsed.Levels;
                    _table.SkillCredits = parsed.SkillCredits;
                    break;
                }
                case ExperienceCsvSection.Attributes:
                    _table.Attributes = ExperienceSectionCsvSerializer.ParseRankSection(csv);
                    break;
                case ExperienceCsvSection.Vitals:
                    _table.Vitals = ExperienceSectionCsvSerializer.ParseRankSection(csv);
                    break;
                case ExperienceCsvSection.TrainedSkills:
                    _table.TrainedSkills = ExperienceSectionCsvSerializer.ParseRankSection(csv);
                    break;
                case ExperienceCsvSection.SpecializedSkills:
                    _table.SpecializedSkills = ExperienceSectionCsvSerializer.ParseRankSection(csv);
                    break;
            }

            PopulateCollections();
            _portalDoc.SetEntry(ExperienceTableId, _table);
            await PersistPortalAsync(ct);
            StatusText = $"Imported {sectionLabel} from {Path.GetFileName(path)}. Use File → Export Dats for client_portal.dat.";
        }
        catch (FormatException ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
        catch (Exception ex) {
            await _dialogService.ShowMessageBoxAsync(null, ex.Message, "Import failed");
        }
    }
}
