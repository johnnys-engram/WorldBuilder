using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using HanumanInstitute.MvvmDialogs;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Layout;

public partial class LayoutEditorViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDocumentManager _documentManager;
    private readonly IDatReaderWriter _dats;
    private DocumentRental<LayoutDatDocument>? _layoutRental;
    private LayoutDatDocument? _layoutDoc;
    private uint[] _allLayoutIds = Array.Empty<uint>();
    private bool _initialized;

    [ObservableProperty] private string _statusText = "Loading layout editor…";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<LayoutListItem> _filteredLayouts = new();
    [ObservableProperty] private LayoutListItem? _selectedLayout;
    [ObservableProperty] private LayoutDetailViewModel? _selectedDetail;
    [ObservableProperty] private bool _isLayoutEditingEnabled;

    public WorldBuilderSettings Settings { get; }

    public IDatReaderWriter Dats => _dats;

    public LayoutEditorViewModel(WorldBuilderSettings settings, Project project, IDocumentManager documentManager,
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
            StatusText = "Read-only project — viewing layouts only; saving is disabled.";
            LoadLayoutIds();
            IsLayoutEditingEnabled = false;
            _initialized = true;
            return;
        }

        var rentResult = await _documentManager.RentDocumentAsync<LayoutDatDocument>(LayoutDatDocument.DocumentId, null, ct);
        if (!rentResult.IsSuccess) {
            StatusText = $"Could not open layout document: {rentResult.Error.Message}";
            IsLayoutEditingEnabled = false;
            _initialized = true;
            return;
        }

        _layoutRental = rentResult.Value;
        _layoutDoc = _layoutRental.Document;
        await _layoutDoc.InitializeForEditingAsync(_dats, _documentManager, null, ct);
        LoadLayoutIds();
        IsLayoutEditingEnabled = true;
        SaveLayoutToProjectCommand.NotifyCanExecuteChanged();
        _initialized = true;
    }

    private async Task PersistLayoutAsync(CancellationToken ct) {
        if (_layoutRental == null) return;
        _layoutRental.Document.Version++;
        var persist = await _documentManager.PersistDocumentAsync(_layoutRental, null, ct);
        if (persist.IsFailure) {
            StatusText = $"Save to project failed: {persist.Error.Message}";
        }
    }

    private void LoadLayoutIds() {
        try {
            _allLayoutIds = _dats.Language.GetAllIdsOfType<LayoutDesc>().OrderBy(id => id).ToArray();
            StatusText = $"Found {_allLayoutIds.Length} UI layouts";
        }
        catch (Exception ex) {
            StatusText = $"Failed to load layout IDs: {ex.Message}";
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) {
        ApplyFilter();
    }

    private void ApplyFilter() {
        var query = SearchText?.Trim().ToUpperInvariant() ?? "";
        IEnumerable<uint> results = _allLayoutIds;

        if (!string.IsNullOrEmpty(query)) {
            var hex = query.TrimStart('0', 'X');
            results = results.Where(id => id.ToString("X8").Contains(hex));
        }

        var items = results.Take(500)
            .Select(id => new LayoutListItem(id, _layoutDoc != null && _layoutDoc.HasStoredLayout(id)))
            .ToList();

        FilteredLayouts = new ObservableCollection<LayoutListItem>(items);
    }

    partial void OnSelectedLayoutChanged(LayoutListItem? value) {
        if (value == null) {
            SelectedDetail = null;
            return;
        }

        try {
            LayoutDesc? source = null;
            bool fromProject = false;
            if (_layoutDoc != null && _layoutDoc.TryGetLayout(value.Id, out var overlay) && overlay != null) {
                source = overlay;
                fromProject = true;
            }
            else if (_dats.Language.TryGet<LayoutDesc>(value.Id, out var fromDat) && fromDat != null) {
                source = fromDat;
            }

            if (source != null) {
                var unpackDb = _dats.Language.Db;
                var working = LayoutDescBinary.Clone(source, value.Id, unpackDb);
                SelectedDetail = new LayoutDetailViewModel(value.Id, working, _dats, IsLayoutEditingEnabled);
                _ = SelectedDetail.LoadPreviewTexturesAsync();
                var src = fromProject ? "project override" : "client DAT";
                StatusText = $"Layout 0x{value.Id:X8} ({src}): {working.Width}x{working.Height}, {working.Elements.Count} elements";
            }
            else {
                SelectedDetail = null;
                StatusText = $"Failed to read layout 0x{value.Id:X8}";
            }
        }
        catch (Exception ex) {
            SelectedDetail = null;
            StatusText = $"Error reading layout: {ex.Message}";
        }
    }

    partial void OnSelectedDetailChanged(LayoutDetailViewModel? value) {
        SaveLayoutToProjectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveLayoutToProject))]
    private async Task SaveLayoutToProjectAsync(CancellationToken ct) {
        if (!IsLayoutEditingEnabled || SelectedDetail == null || _layoutDoc == null) {
            StatusText = "Nothing to save";
            return;
        }

        _layoutDoc.SetLayout(SelectedDetail.LayoutId, SelectedDetail.WorkingLayout);
        await PersistLayoutAsync(ct);
        StatusText = $"Saved layout {SelectedDetail.LayoutIdHex} to project — export DATs to write language/local DATs.";
        ApplyFilter();
    }

    private bool CanSaveLayoutToProject() => IsLayoutEditingEnabled && SelectedDetail != null && _layoutDoc != null;
}

public class LayoutListItem {
    public uint Id { get; }
    public string DisplayId { get; }
    public bool HasProjectOverride { get; }

    public LayoutListItem(uint id, bool hasProjectOverride = false) {
        Id = id;
        HasProjectOverride = hasProjectOverride;
        DisplayId = hasProjectOverride ? $"0x{id:X8}  *" : $"0x{id:X8}";
    }

    public override string ToString() => DisplayId;
}

public partial class LayoutDetailViewModel : ObservableObject {
    private readonly IDatReaderWriter _dats;
    private readonly bool _canEdit;

    public uint LayoutId { get; }
    public string LayoutIdHex { get; }

    public uint Width => WorkingLayout.Width;
    public uint Height => WorkingLayout.Height;

    public LayoutDesc WorkingLayout { get; }

    public ObservableCollection<ElementTreeNode> RootElements { get; } = new();

    [ObservableProperty] private ElementTreeNode? _selectedElement;

    [ObservableProperty] private IReadOnlyDictionary<uint, WriteableBitmap?>? _elementTextures;

    [ObservableProperty] private string _elementGeomXText = "0";
    [ObservableProperty] private string _elementGeomYText = "0";
    [ObservableProperty] private string _elementGeomWidthText = "0";
    [ObservableProperty] private string _elementGeomHeightText = "0";
    [ObservableProperty] private string _elementGeomZText = "0";
    [ObservableProperty] private string _elementGeomLeftText = "0";
    [ObservableProperty] private string _elementGeomTopText = "0";
    [ObservableProperty] private string _elementGeomRightText = "0";
    [ObservableProperty] private string _elementGeomBottomText = "0";
    [ObservableProperty] private string _elementReadOrderText = "0";
    [ObservableProperty] private string _primarySurfaceHexText = "";
    [ObservableProperty] private string _newTemplateImageHexText = "";
    [ObservableProperty] private string _layoutWidthText = "";
    [ObservableProperty] private string _layoutHeightText = "";

    public LayoutDetailViewModel(uint id, LayoutDesc workingLayout, IDatReaderWriter dats, bool canEdit) {
        _dats = dats;
        _canEdit = canEdit;
        WorkingLayout = workingLayout;
        workingLayout.Id = id;
        LayoutId = id;
        LayoutIdHex = $"0x{id:X8}";
        _layoutWidthText = workingLayout.Width.ToString();
        _layoutHeightText = workingLayout.Height.ToString();

        foreach (var kvp in workingLayout.Elements.OrderBy(e => e.Value.ReadOrder)) {
            RootElements.Add(new ElementTreeNode(kvp.Value));
        }
    }

    partial void OnSelectedElementChanged(ElementTreeNode? value) {
        if (value == null) {
            PrimarySurfaceHexText = "";
            return;
        }

        ElementGeomXText = value.Source.X.ToString();
        ElementGeomYText = value.Source.Y.ToString();
        ElementGeomWidthText = value.Source.Width.ToString();
        ElementGeomHeightText = value.Source.Height.ToString();
        ElementGeomZText = value.Source.ZLevel.ToString();
        ElementGeomLeftText = value.Source.LeftEdge.ToString();
        ElementGeomTopText = value.Source.TopEdge.ToString();
        ElementGeomRightText = value.Source.RightEdge.ToString();
        ElementGeomBottomText = value.Source.BottomEdge.ToString();
        ElementReadOrderText = value.Source.ReadOrder.ToString();

        var sid = LayoutMediaHelper.TryPrimarySurfaceForElement(value.Source);
        PrimarySurfaceHexText = sid.HasValue ? $"0x{sid.Value:X8}" : "";
    }

    [RelayCommand]
    private void ApplyLayoutSize() {
        if (!_canEdit) return;
        if (!uint.TryParse(LayoutWidthText, out var w)) return;
        if (!uint.TryParse(LayoutHeightText, out var h)) return;
        if (w == 0 || h == 0) return;
        WorkingLayout.Width = w;
        WorkingLayout.Height = h;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    [RelayCommand]
    private void ApplyElementGeometry() {
        if (!_canEdit || SelectedElement == null) return;
        var el = SelectedElement;

        if (!uint.TryParse(ElementGeomXText, out var x)) return;
        if (!uint.TryParse(ElementGeomYText, out var y)) return;
        if (!uint.TryParse(ElementGeomWidthText, out var w)) return;
        if (!uint.TryParse(ElementGeomHeightText, out var h)) return;
        if (!uint.TryParse(ElementGeomZText, out var z)) return;
        if (!uint.TryParse(ElementGeomLeftText, out var le)) return;
        if (!uint.TryParse(ElementGeomTopText, out var te)) return;
        if (!uint.TryParse(ElementGeomRightText, out var re)) return;
        if (!uint.TryParse(ElementGeomBottomText, out var be)) return;
        if (!uint.TryParse(ElementReadOrderText, out var ro)) return;

        LayoutElementEditHelper.EnsureGeometryIncorporation(el.Source);
        el.Source.X = x;
        el.Source.Y = y;
        el.Source.Width = w;
        el.Source.Height = h;
        el.Source.ZLevel = z;
        el.Source.LeftEdge = le;
        el.Source.TopEdge = te;
        el.Source.RightEdge = re;
        el.Source.BottomEdge = be;
        el.Source.ReadOrder = ro;
        el.NotifySourceMutated();
    }

    [RelayCommand]
    private void ApplyPrimarySurface() {
        if (!_canEdit || SelectedElement == null) return;
        if (!LayoutElementEditHelper.TryParseDatHex(PrimarySurfaceHexText, out var id)) return;
        LayoutElementEditHelper.TrySetPrimaryImageSurface(SelectedElement.Source, id);
        SelectedElement.RefreshStateRows();
        _ = LoadPreviewTexturesAsync();
    }

    [RelayCommand]
    private void AddTemplateImage() {
        if (!_canEdit || SelectedElement == null) return;
        if (!LayoutElementEditHelper.TryParseDatHex(NewTemplateImageHexText, out var id)) return;
        LayoutElementEditHelper.TryAddTemplateImage(SelectedElement.Source, id);
        SelectedElement.RefreshStateRows();
        NewTemplateImageHexText = "";
        _ = LoadPreviewTexturesAsync();
    }

    public async Task LoadPreviewTexturesAsync() {
        var dats = _dats;
        var nodes = new List<ElementTreeNode>();
        foreach (var r in RootElements)
            CollectNodes(r, nodes);

        var map = await Task.Run(() => {
            var d = new Dictionary<uint, WriteableBitmap?>();
            const int maxEdge = 256;
            foreach (var node in nodes) {
                var sid = LayoutMediaHelper.TryPrimarySurfaceForElement(node.Source);
                if (!sid.HasValue) continue;
                WriteableBitmap? bmp = (sid.Value & 0xFF000000) == 0x06000000
                    ? DatIconLoader.LoadIcon(dats, sid.Value, maxEdge)
                    : DatIconLoader.LoadSurfaceIcon(dats, sid.Value, maxEdge);
                if (bmp != null)
                    d[node.ElementId] = bmp;
            }
            return (IReadOnlyDictionary<uint, WriteableBitmap?>)d;
        }).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() => ElementTextures = map);
    }

    static void CollectNodes(ElementTreeNode n, List<ElementTreeNode> acc) {
        acc.Add(n);
        foreach (var c in n.Children)
            CollectNodes(c, acc);
    }
}

public partial class ElementTreeNode : ObservableObject {
    public ElementDesc Source { get; }

    public uint ElementId => Source.ElementId;
    public string DisplayId => $"0x{Source.ElementId:X}";
    public uint Type => Source.Type;
    public string TypeHex => Source.Type != 0 ? $"0x{Source.Type:X8}" : "inherit";
    public uint X => Source.X;
    public uint Y => Source.Y;
    public uint Width => Source.Width;
    public uint Height => Source.Height;
    public uint ZLevel => Source.ZLevel;
    public uint BaseElement => Source.BaseElement;
    public uint BaseLayoutId => Source.BaseLayoutId;
    public string BaseLayoutHex => Source.BaseLayoutId != 0 ? $"0x{Source.BaseLayoutId:X8}" : "none";
    public uint LeftEdge => Source.LeftEdge;
    public uint TopEdge => Source.TopEdge;
    public uint RightEdge => Source.RightEdge;
    public uint BottomEdge => Source.BottomEdge;
    public uint ReadOrder => Source.ReadOrder;
    public int StatesCount => Source.States?.Count ?? 0;
    public int ChildrenCount => Source.Children?.Count ?? 0;

    public string DefaultStateHex => $"0x{(uint)Source.DefaultState:X8}";

    public string Summary => $"#{DisplayId} {TypeHex} ({Source.Width}x{Source.Height})";

    public ObservableCollection<LayoutStateRow> StateRows { get; } = new();

    public ObservableCollection<ElementTreeNode> Children { get; } = new();

    public ElementTreeNode(ElementDesc element) {
        Source = element;
        LayoutMediaHelper.PopulateStateRows(element, StateRows);

        if (element.Children != null) {
            foreach (var kvp in element.Children.OrderBy(c => c.Value.ReadOrder)) {
                Children.Add(new ElementTreeNode(kvp.Value));
            }
        }
    }

    public void SetPosition(uint x, uint y) {
        LayoutElementEditHelper.EnsureGeometryIncorporation(Source);
        if (Source.X == x && Source.Y == y) return;
        Source.X = x;
        Source.Y = y;
        NotifySourceMutated();
    }

    public void SetBounds(uint x, uint y, uint width, uint height) {
        LayoutElementEditHelper.EnsureGeometryIncorporation(Source);
        width = System.Math.Max(1u, width);
        height = System.Math.Max(1u, height);
        if (Source.X == x && Source.Y == y && Source.Width == width && Source.Height == height) return;
        Source.X = x;
        Source.Y = y;
        Source.Width = width;
        Source.Height = height;
        NotifySourceMutated();
    }

    public void NotifySourceMutated() {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ZLevel));
        OnPropertyChanged(nameof(LeftEdge));
        OnPropertyChanged(nameof(TopEdge));
        OnPropertyChanged(nameof(RightEdge));
        OnPropertyChanged(nameof(BottomEdge));
        OnPropertyChanged(nameof(ReadOrder));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatesCount));
        OnPropertyChanged(nameof(ChildrenCount));
    }

    public void RefreshStateRows() {
        StateRows.Clear();
        LayoutMediaHelper.PopulateStateRows(Source, StateRows);
        OnPropertyChanged(nameof(StatesCount));
    }
}
