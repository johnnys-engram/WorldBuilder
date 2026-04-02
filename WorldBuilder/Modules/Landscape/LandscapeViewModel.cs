using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HanumanInstitute.MvvmDialogs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib.Input;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using WorldBuilder.Modules.Landscape.Lib;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Lib.AceDb;
using WorldBuilder.ViewModels;
using ICamera = WorldBuilder.Shared.Models.ICamera;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeViewModel : ViewModelBase, ILandscapeRaycastService, ILandscapeEditorService, IDisposable, IToolModule, IHotkeyHandler {
    private readonly IProject _project;
    private readonly IDatReaderWriter _dats;
    private readonly IPortalService _portalService;
    private readonly ILogger<LandscapeViewModel> _log;
    private readonly IDialogService _dialogService;
    private readonly BookmarksManager _bookmarksManager;
    private readonly ILandscapeObjectService _landscapeObjectService;
    private readonly WorldBuilder.Shared.Lib.IInputManager _inputManager;
    private readonly IKeywordRepositoryService _keywordRepository;
    private readonly ProjectManager _projectManager;
    private readonly ThemeService _themeService;
    private DocumentRental<LandscapeDocument>? _landscapeRental;

    public string Name => "Landscape";
    public ViewModelBase ViewModel => this;

    [ObservableProperty] private LandscapeDocument? _activeDocument;
    public IDatReaderWriter Dats => _dats;

    /// <summary>
    /// Gets a value indicating whether the current project is read-only.
    /// </summary>
    public bool IsReadOnly => _project.IsReadOnly;

    public ObservableCollection<ILandscapeTool> Tools { get; } = new();

    [ObservableProperty]
    private ILandscapeTool? _activeTool;

    [ObservableProperty]
    private LandscapeLayer? _activeLayer;

    [ObservableProperty] private Vector3 _brushPosition;
    [ObservableProperty] private float _brushRadius = 30f;
    [ObservableProperty] private BrushShape _brushShape = BrushShape.Circle;
    [ObservableProperty] private bool _showBrush;

    [ObservableProperty] private bool _is3DCameraEnabled = true;

    /// <summary>Minimap zoom: &gt;1 shows fewer world units (closer view). Default slightly zoomed in.</summary>
    [ObservableProperty] private double _minimapZoom = 1.35;

    /// <summary>Larger, brighter encounter dots on the minimap.</summary>
    [ObservableProperty] private bool _minimapEncounterHighlight;

    [ObservableProperty] private Avalonia.Controls.GridLength _bottomPanelHeight = new Avalonia.Controls.GridLength(0);

    [RelayCommand]
    private void MinimapZoomIn() => MinimapZoom = Math.Min(6.0, MinimapZoom * 1.2);

    [RelayCommand]
    private void MinimapZoomOut() => MinimapZoom = Math.Max(0.55, MinimapZoom / 1.2);

    private void OnIsDebugShapesEnabledChanged(bool value) {
        EditorState.ShowDebugShapes = value;
        if (ActiveTool is InspectorTool inspector) {
            inspector.ShowBoundingBoxes = value;
        }
    }

    partial void OnIs3DCameraEnabledChanged(bool value) {
        UpdateToolContext();
    }

    private readonly WorldBuilderSettings _settings;
    public EditorState EditorState { get; } = new();

    public CommandHistory CommandHistory { get; } = new();
    public HistoryPanelViewModel HistoryPanel { get; }
    public LayersPanelViewModel LayersPanel { get; }
    public BookmarksPanelViewModel BookmarksPanel { get; }
    public PropertiesPanelViewModel PropertiesPanel { get; }
    public SetupBrowserPanelViewModel SetupBrowserPanel { get; }

    public bool IsObjectToolActive => ActiveTool is ObjectManipulationTool;

    private readonly IDocumentManager _documentManager;
    private readonly LandscapeSettingsBridge _settingsBridge;
    private readonly DebouncedAction<ObjectId> _commitDebounce;
    private readonly DebouncedAction<string> _saveDebounce;
    private readonly ToolSettingsProvider _toolSettingsProvider;

    private LandscapeToolContext? _toolContext;
    private WorldBuilder.Shared.Models.ICamera? _camera;

    // ACE encounter incremental-loading state.
    // SemaphoreSlim ensures only one load runs at a time (command + background tick share it).
    private readonly SemaphoreSlim _encounterLoadLock = new(1, 1);
    // Landblock IDs whose encounter overlay has been set on the renderer.
    private readonly HashSet<ushort> _loadedEncounterLbIds = new();
    // Per-landblock dot lists for the minimap.
    private readonly Dictionary<ushort, List<EncounterMapDot>> _encounterDotsByLb = new();
    private CancellationTokenSource? _encounterTickCts;
    public WorldBuilder.Shared.Models.ICamera? Camera {
        get => _gameScene?.CurrentCamera ?? _camera;
        set => _camera = value;
    }

    public GameScene GameScene => _gameScene!;

    public LandscapeViewModel(IProject project, IDatReaderWriter dats, IPortalService portalService, IDocumentManager documentManager, BookmarksManager bookmarksManager, ILogger<LandscapeViewModel> log, IDialogService dialogService, WorldBuilderSettings settings, WorldBuilder.Shared.Lib.IInputManager inputManager, ILandscapeObjectService landscapeObjectService, IKeywordRepositoryService keywordRepository, ProjectManager projectManager, ThemeService themeService) {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        _bookmarksManager = bookmarksManager ?? throw new ArgumentNullException(nameof(bookmarksManager));
        _landscapeObjectService = landscapeObjectService ?? throw new ArgumentNullException(nameof(landscapeObjectService));
        _keywordRepository = keywordRepository ?? throw new ArgumentNullException(nameof(keywordRepository));
        _projectManager = projectManager ?? throw new ArgumentNullException(nameof(projectManager));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        _toolSettingsProvider = new ToolSettingsProvider(_settings.Project!);
        _settingsBridge = new LandscapeSettingsBridge(_settings, EditorState);
        _commitDebounce = new DebouncedAction<ObjectId>(TimeSpan.FromMilliseconds(500));
        _saveDebounce = new DebouncedAction<string>(TimeSpan.FromMilliseconds(2000));

        CommandHistory.MaxHistoryDepth = _settings.App.HistoryLimit;
        CommandHistory.OnChange += OnCommandHistoryChanged;

        HistoryPanel = new HistoryPanelViewModel(CommandHistory);
        PropertiesPanel = new PropertiesPanelViewModel {
            Dats = dats,
            DeleteCommand = new RelayCommand(() => {
                if (ActiveTool is ObjectManipulationTool objTool) {
                    objTool.DeleteSelection();
                }
            })
        };
        PropertiesPanel.OnSelectedItemPropertyChanged += OnSelectedObjectPropertyChanged;
        LayersPanel = new LayersPanelViewModel(log, CommandHistory, _documentManager, _settings, _project, async (item, changeType) => {
            if (ActiveDocument != null) {
                if (changeType == LayerChangeType.VisibilityChange && item != null) {
                    await ActiveDocument.SetLayerVisibilityAsync(item.Model.Id, item.IsVisible);
                }
                else {
                    if (changeType == LayerChangeType.PropertyChange) {
                        RequestSave(ActiveDocument.Id);
                    }

                    await ActiveDocument.LoadMissingLayersAsync(_documentManager, default);
                }
            }
        });

        LayersPanel.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(LayersPanel.SelectedItem)) {
                ActiveLayer = LayersPanel.SelectedItem?.Model as LandscapeLayer;
                PropertiesPanel.SelectedItem = LayersPanel.SelectedItem;
            }
        };
        BookmarksPanel = new BookmarksPanelViewModel(_settings!, _inputManager, _bookmarksManager, this, _dialogService);

        _ = LoadLandscapeAsync();

        var objTool = new ObjectManipulationTool(this, this, _landscapeObjectService, _toolSettingsProvider, _inputManager);

        // Register Tools
        if (!_project.IsReadOnly) {
            Tools.Add(new BrushTool(this, this, _landscapeObjectService, _toolSettingsProvider, _inputManager));
            Tools.Add(new BucketFillTool(this, this, _landscapeObjectService, _toolSettingsProvider));
            Tools.Add(new RoadVertexTool(this, this, _landscapeObjectService, _toolSettingsProvider));
            Tools.Add(new RoadLineTool(this, this, _landscapeObjectService, _toolSettingsProvider));
            Tools.Add(objTool);
            Tools.Add(new InspectorTool(this, this, _landscapeObjectService, _toolSettingsProvider));
        }

        SetupBrowserPanel = new SetupBrowserPanelViewModel(_keywordRepository, _projectManager, _dats, _settings, _themeService, objTool);

        ActiveTool = Tools.FirstOrDefault();
        PropertiesPanel.IsEditable = ActiveTool is ObjectManipulationTool;

        // Initialize bottom panel height from settings
        _bottomPanelHeight = IsObjectToolActive ? new Avalonia.Controls.GridLength(_settings.Landscape.BottomPanelHeight, Avalonia.Controls.GridUnitType.Pixel) : new Avalonia.Controls.GridLength(0);

        _settings.Landscape.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(LandscapeEditorSettings.BottomPanelHeight) && IsObjectToolActive) {
                BottomPanelHeight = new Avalonia.Controls.GridLength(_settings.Landscape.BottomPanelHeight, Avalonia.Controls.GridUnitType.Pixel);
            }
        };

        this.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(IsObjectToolActive)) {
                BottomPanelHeight = IsObjectToolActive ? new Avalonia.Controls.GridLength(_settings.Landscape.BottomPanelHeight, Avalonia.Controls.GridUnitType.Pixel) : new Avalonia.Controls.GridLength(0);
            }
            else if (e.PropertyName == nameof(BottomPanelHeight)) {
                if (BottomPanelHeight.IsAbsolute && IsObjectToolActive) {
                    _settings.Landscape.BottomPanelHeight = BottomPanelHeight.Value;
                }
            }
        };
    }

    partial void OnActiveToolChanged(ILandscapeTool? oldValue, ILandscapeTool? newValue) {
        PropertiesPanel.IsEditable = newValue is ObjectManipulationTool;
        if (oldValue is InspectorTool oldInspector) {
            oldInspector.PropertyChanged -= OnInspectorToolPropertyChanged;
        }
        if (oldValue is INotifyPropertyChanged oldNotify) {
            oldNotify.PropertyChanged -= OnToolPropertyChanged;
        }
        if (oldValue?.Brush != null) {
            oldValue.Brush.PropertyChanged -= OnBrushPropertyChanged;
        }

        oldValue?.Deactivate();

        // Clear selection and hover when switching tools
        _toolContext?.NotifyInspectorSelected(SceneRaycastHit.NoHit);
        _toolContext?.NotifyInspectorHovered(SceneRaycastHit.NoHit);

        if (newValue is InspectorTool newInspector) {
            newInspector.PropertyChanged += OnInspectorToolPropertyChanged;
            IsDebugShapesEnabled = newInspector.ShowBoundingBoxes;
        }
        else if (newValue is ObjectManipulationTool) {
            // Enable debug shapes for gizmo rendering
            IsDebugShapesEnabled = true;
        }
        else {
            IsDebugShapesEnabled = false;
        }

        if (newValue is INotifyPropertyChanged newNotify) {
            newNotify.PropertyChanged += OnToolPropertyChanged;
        }

        if (newValue?.Brush != null) {
            newValue.Brush.PropertyChanged += OnBrushPropertyChanged;
        }

        SyncBrushFromTool(newValue);

        if (newValue != null && _toolContext != null) {
            newValue.Activate(_toolContext);
        }

        OnPropertyChanged(nameof(IsObjectToolActive));

        _gameScene?.SetActiveTool(newValue);
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is ILandscapeTool tool) {
            if (e.PropertyName == nameof(ILandscapeTool.Brush)) {
                if (tool.Brush != null) {
                    tool.Brush.PropertyChanged += OnBrushPropertyChanged;
                }
                SyncBrushFromTool(tool);
            }
        }
    }

    private void OnBrushPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is ILandscapeBrush brush && ActiveTool?.Brush == brush) {
            SyncBrushFromTool(ActiveTool);
        }
    }

    private void SyncBrushFromTool(ILandscapeTool? tool) {
        if (tool?.Brush == null) {
            ShowBrush = false;
            return;
        }

        ShowBrush = tool.Brush.IsVisible;
        BrushPosition = tool.Brush.Position;
        BrushRadius = tool.Brush.Radius;
        BrushShape = tool.Brush.Shape;
    }

    private void OnInspectorToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is InspectorTool inspector && e.PropertyName == nameof(InspectorTool.ShowBoundingBoxes)) {
            IsDebugShapesEnabled = inspector.ShowBoundingBoxes;
        }
    }

    private Action<int, int>? _invalidateCallback;
    private Task _updateTask = Task.CompletedTask;
    private readonly object _updateTaskLock = new object();

    partial void OnActiveDocumentChanged(LandscapeDocument? oldValue, LandscapeDocument? newValue) {
        _log.LogTrace("LandscapeViewModel.OnActiveDocumentChanged: Syncing layers for doc {DocId}", newValue?.Id);

        LayersPanel.SyncWithDocument(newValue);

        // Set first base layer as active by default
        if (newValue != null && ActiveLayer == null) {
            ActiveLayer = newValue.GetAllLayers().FirstOrDefault(l => l.IsBase);
        }
        else if (ActiveLayer != null) {
            LayersPanel.SelectedItem = LayersPanel.FindVM(ActiveLayer.Id);
        }

        if (newValue != null && Camera != null) {
            _log.LogTrace("LandscapeViewModel.OnActiveDocumentChanged: Re-initializing context");
            UpdateToolContext();
            RestoreCameraState();
        }
    }

    partial void OnActiveLayerChanged(LandscapeLayer? oldValue, LandscapeLayer? newValue) {
        _log.LogTrace("LandscapeViewModel.OnActiveLayerChanged: New layer {LayerId}", newValue?.Id);
        if (newValue != null && (LayersPanel.SelectedItem == null || LayersPanel.SelectedItem.Model.Id != newValue.Id)) {
            LayersPanel.SelectedItem = LayersPanel.FindVM(newValue.Id);
        }
        UpdateToolContext();
    }

    private void UpdateToolContext() {
        if (ActiveDocument != null && Camera != null) {
            _log.LogTrace("Updating tool context. ActiveLayer: {LayerId}", ActiveLayer?.Id);

            if (_toolContext != null) {
                _toolContext.InspectorHovered -= OnInspectorHovered;
                _toolContext.InspectorSelected -= OnInspectorSelected;
            }

            if (_settings?.Project != null) {
                _toolContext = new LandscapeToolContext(ActiveDocument, EditorState, _dats, CommandHistory, Camera, _log, _landscapeObjectService, this, this, _toolSettingsProvider, ActiveLayer);
            }
            else {
                // Fallback or log if project is null?
                _log.LogWarning("Cannot update tool context: Project is null");
                return;
            }
            _toolContext.InspectorHovered += OnInspectorHovered;
            _toolContext.InspectorSelected += OnInspectorSelected;

            _gameScene?.SetActiveTool(ActiveTool);

            ActiveTool?.Deactivate();
            ActiveTool?.Activate(_toolContext);
        }
        else {
            _log.LogTrace("Skipping UpdateToolContext. ActiveDocument: {HasDoc}, Camera: {HasCamera}", ActiveDocument != null, Camera != null);
        }
    }


    private void OnInspectorHovered(object? sender, InspectorSelectionEventArgs e) {
        _gameScene?.SetHoveredObject(e.Selection.Type, e.Selection.LandblockId, e.Selection.InstanceId, e.Selection.ObjectId, e.Selection.VertexX, e.Selection.VertexY);
    }

    private void OnInspectorSelected(object? sender, InspectorSelectionEventArgs e) {
        var lbId = e.Selection.LandblockId;
        _gameScene?.SetSelectedObject(e.Selection.Type, lbId, e.Selection.InstanceId, e.Selection.ObjectId, e.Selection.VertexX, e.Selection.VertexY);

        // Check if the selection is logically the same to avoid re-templating and focus loss
        if (PropertiesPanel.SelectedItem is ISelectedObjectInfo current) {
            bool isSameObject = e.Selection.InstanceId != ObjectId.Empty && current.InstanceId == e.Selection.InstanceId;
            bool isSameVertex = e.Selection.Type == ObjectType.Vertex && current is LandscapeVertexViewModel v && v.VertexX == e.Selection.VertexX && v.VertexY == e.Selection.VertexY;
            
            // If the ID changed but it was our object, it might have been a move between landblocks
            if (!isSameObject && e.Selection.InstanceId != ObjectId.Empty && current.InstanceId != ObjectId.Empty && e.Selection.ObjectId == current.ObjectId) {
                // If it's the same SetupId and it's currently being "debounced" or just moved, 
                // we treat it as the same object to preserve focus, BUT only if the type is the same.
                // If the type changed (e.g. from static to cell object), we need to re-template.
                if (current.Type == e.Selection.Type) {
                    isSameObject = true;
                }
            }

            if (isSameObject || isSameVertex) {
                // Already selected, update ID and position/rotation
                _isUpdatingFromSelection = true;
                try {
                    current.InstanceId = e.Selection.InstanceId;
                    current.Position = e.Selection.Position;
                    current.Rotation = e.Selection.Rotation;
                    current.LandblockId = lbId;
                    current.LocalPosition = e.Selection.LocalPosition;
                    current.CellId = e.Selection.CellId;
                }
                finally {
                    _isUpdatingFromSelection = false;
                }
                return;
            }
        }

        // Auto-select layer if an object is selected
        if (e.Selection.Type == ObjectType.StaticObject ||
            e.Selection.Type == ObjectType.Building ||
            e.Selection.Type == ObjectType.Scenery ||
            e.Selection.Type == ObjectType.EnvCellStaticObject ||
            e.Selection.Type == ObjectType.EnvCell ||
            e.Selection.Type == ObjectType.Portal) {
            var layerId = _landscapeObjectService.GetStaticObjectLayerId(ActiveDocument!, lbId, e.Selection.InstanceId);
            if (!string.IsNullOrEmpty(layerId)) {
                var layerVM = LayersPanel.FindVM(layerId);
                if (layerVM != null) {
                    LayersPanel.SelectedItem = layerVM;
                }
            }
        }

        if (e.Selection.Type == ObjectType.StaticObject || e.Selection.Type == ObjectType.Building) {
            if (e.Selection.Type == ObjectType.StaticObject) {
                PropertiesPanel.SelectedItem = new StaticObjectViewModel(e.Selection.ObjectId, e.Selection.InstanceId, lbId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, e.Selection.LayerId);
            }
            else if (e.Selection.Type == ObjectType.Building) {
                PropertiesPanel.SelectedItem = new BuildingViewModel(e.Selection.ObjectId, e.Selection.InstanceId, lbId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, e.Selection.LayerId);
            }
        }
        else if (e.Selection.Type == ObjectType.Scenery) {
            PropertiesPanel.SelectedItem = new SceneryViewModel(e.Selection.ObjectId, e.Selection.InstanceId, lbId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, e.Selection.DisqualificationReason, e.Selection.LayerId);
        }
        else if (e.Selection.Type == ObjectType.Portal) {
            uint cellId = e.Selection.ObjectId; // For portals, ObjectId is the parent CellId
            PropertiesPanel.SelectedItem = new PortalViewModel(lbId, cellId, e.Selection.InstanceId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, ActiveDocument?.CellDatabase, e.Selection.LayerId);
        }
        else if (e.Selection.Type == ObjectType.EnvCell) {
            PropertiesPanel.SelectedItem = new EnvCellViewModel(e.Selection.ObjectId, e.Selection.InstanceId, lbId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, ActiveDocument?.CellDatabase, e.Selection.LayerId);
        }
        else if (e.Selection.Type == ObjectType.EnvCellStaticObject) {
            uint cellId = e.Selection.InstanceId.Context;
            PropertiesPanel.SelectedItem = new EnvCellStaticObjectViewModel(e.Selection.ObjectId, e.Selection.InstanceId, lbId, cellId, e.Selection.Position, e.Selection.LocalPosition, e.Selection.Rotation, e.Selection.LayerId);
        }
        else if (e.Selection.Type == ObjectType.Vertex) {
            PropertiesPanel.SelectedItem = new LandscapeVertexViewModel(e.Selection.VertexX, e.Selection.VertexY, ActiveDocument!, _dats, CommandHistory);
        }
        else {
            PropertiesPanel.SelectedItem = null;
        }
    }

    private bool _isUpdatingFromSelection;
    private Task ExecuteDocumentCommandAsync<T>(T command) where T : BaseCommand {
        if (ActiveDocument == null) return Task.CompletedTask;

        lock (_updateTaskLock) {
            _updateTask = _updateTask.ContinueWith(async t => {
                var result = await _documentManager.ApplyLocalEventAsync(command, null!, default);
                if (result.IsSuccess) {
                    RequestSave(ActiveDocument.Id.ToString());
                }
                else {
                    _log.LogError("Failed to execute document command {CommandType}: {Error}", typeof(T).Name, result.Error);
                }
            }, TaskScheduler.Default).Unwrap();
            return _updateTask;
        }
    }

    public void UpdateStaticObject(string layerId, ushort oldLbId, StaticObject oldObject, ushort newLbId, StaticObject newObj) {
        _ = ExecuteDocumentCommandAsync(new UpdateStaticObjectCommand {
            TerrainDocumentId = ActiveDocument?.Id.ToString() ?? "",
            LayerId = layerId,
            OldLandblockId = oldLbId,
            NewLandblockId = newLbId,
            OldObject = oldObject,
            NewObject = newObj,
            UserId = ""
        });
    }

    public void AddStaticObject(string layerId, ushort landblockId, StaticObject obj) {
        _ = ExecuteDocumentCommandAsync(new AddStaticObjectCommand {
            TerrainDocumentId = ActiveDocument?.Id.ToString() ?? "",
            LayerId = layerId,
            LandblockId = landblockId,
            Object = obj,
            UserId = ""
        });
    }

    public void DeleteStaticObject(string layerId, ushort landblockId, StaticObject obj) {
        _ = ExecuteDocumentCommandAsync(new DeleteStaticObjectCommand {
            TerrainDocumentId = ActiveDocument?.Id.ToString() ?? "",
            LayerId = layerId,
            LandblockId = landblockId,
            InstanceId = obj.InstanceId,
            PreviousState = obj,
            UserId = ""
        });
    }

    #region ILandscapeRaycastService Implementation
    public bool RaycastStaticObject(Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, ObjectId ignoreInstanceId = default) {
        hit = SceneRaycastHit.NoHit;
        return _gameScene?.RaycastStaticObjects(origin, direction, includeBuildings, includeStaticObjects, out hit, false, float.MaxValue, ignoreInstanceId) ?? false;
    }

    public bool RaycastScenery(Vector3 origin, Vector3 direction, out SceneRaycastHit hit) {
        hit = SceneRaycastHit.NoHit;
        return _gameScene?.RaycastScenery(origin, direction, out hit) ?? false;
    }

    public bool RaycastPortals(Vector3 origin, Vector3 direction, out SceneRaycastHit hit) {
        hit = SceneRaycastHit.NoHit;
        return _gameScene?.RaycastPortals(origin, direction, out hit) ?? false;
    }

    public bool RaycastEnvCells(Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, ObjectId ignoreInstanceId = default) {
        hit = SceneRaycastHit.NoHit;
        return _gameScene?.RaycastEnvCells(origin, direction, includeCells, includeStaticObjects, out hit, false, float.MaxValue, ignoreInstanceId) ?? false;
    }

    public TerrainRaycastHit RaycastTerrain(float screenX, float screenY, Vector2 viewportSize, ICamera camera) {
        if (_gameScene == null || ActiveDocument?.Region == null) return new TerrainRaycastHit();
        return TerrainRaycast.Raycast(screenX, screenY, (int)viewportSize.X, (int)viewportSize.Y, camera, ActiveDocument.Region, ActiveDocument);
    }
    #endregion

    public void InvalidateLandblock(int x, int y) => _invalidateCallback?.Invoke(x, y);

    private async void OnSelectedObjectPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_isUpdatingFromSelection || sender is not ISelectedObjectInfo info || _toolContext == null || ActiveDocument?.Region == null) return;

        if (e.PropertyName == nameof(ISelectedObjectInfo.LocalPosition) || e.PropertyName == nameof(ISelectedObjectInfo.Rotation) ||
            e.PropertyName == "X" || e.PropertyName == "Y" || e.PropertyName == "Z" ||
            e.PropertyName == "RotationX" || e.PropertyName == "RotationY" || e.PropertyName == "RotationZ") {

            // Calculate world position
            var worldPos = _landscapeObjectService.ComputeWorldPosition(ActiveDocument.Region, info.LandblockId, info.LocalPosition);
            info.Position = worldPos;

            // Preview in real-time
            uint? currentCellId = GetEnvCellAt(worldPos);
            if (currentCellId == 0) currentCellId = null;
            
            _isUpdatingFromSelection = true;
            try {
                info.CellId = currentCellId;
            }
            finally {
                _isUpdatingFromSelection = false;
            }

            NotifyObjectPositionPreview(info.LandblockId, info.InstanceId, worldPos, info.Rotation, currentCellId ?? 0);

            // Debounce the actual commit
            RequestCommitObjectChange(info);
        }
    }

    public void NotifyObjectPositionPreview(ushort landblockId, ObjectId instanceId, Vector3 position, Quaternion rotation, uint currentCellId, uint modelId = 0) {
        _gameScene?.UpdateObjectPreview(landblockId, instanceId, position, rotation, currentCellId, modelId);

        // Don't update UI for a clear signal
        if (position == Vector3.Zero && rotation == Quaternion.Identity && currentCellId == 0) return;

        // Update PropertiesPanel in real-time
        if (PropertiesPanel.SelectedItem is ISelectedObjectInfo info && info.InstanceId == instanceId && ActiveDocument?.Region != null) {
            _isUpdatingFromSelection = true;
            try {
                // Recalculate local position relative to the landblock origin
                var lbOrigin = _landscapeObjectService.ComputeWorldPosition(ActiveDocument.Region, landblockId, Vector3.Zero);
                var localPos = position - lbOrigin;

                // Check for type transition between static types to force UI re-templating
                var targetType = currentCellId != 0 ? ObjectType.EnvCellStaticObject : ObjectType.StaticObject;
                if (info.Type != targetType && (info.Type == ObjectType.StaticObject || info.Type == ObjectType.EnvCellStaticObject)) {
                    if (targetType == ObjectType.EnvCellStaticObject) {
                        PropertiesPanel.SelectedItem = new EnvCellStaticObjectViewModel(info.ObjectId, info.InstanceId, landblockId, currentCellId, position, localPos, rotation, info.LayerId);
                    } else {
                        PropertiesPanel.SelectedItem = new StaticObjectViewModel(info.ObjectId, info.InstanceId, landblockId, position, localPos, rotation, info.LayerId);
                    }
                    return;
                }

                info.LandblockId = landblockId;
                info.CellId = currentCellId != 0 ? currentCellId : null;
                info.Position = position;
                info.LocalPosition = localPos;
                info.Rotation = rotation;

                if (info is SelectedObjectViewModelBase vm) {
                    vm.Type = targetType;
                }
                else if (info is SceneRaycastHit hit) {
                    hit.Type = targetType;
                    PropertiesPanel.SelectedItem = hit; // Trigger UI refresh if needed
                }
            }
            finally {
                _isUpdatingFromSelection = false;
            }
        }
    }

    public BoundingBox? GetStaticObjectBounds(ushort landblockId, ObjectId instanceId) => _gameScene?.GetStaticObjectBounds(landblockId, instanceId);
    public BoundingBox? GetStaticObjectLocalBounds(ushort landblockId, ObjectId instanceId) => _gameScene?.GetStaticObjectLocalBounds(landblockId, instanceId);
    public BoundingBox? GetModelBounds(uint modelId) => _gameScene?.GetModelBounds(modelId);
    public (Vector3 position, Quaternion rotation, Vector3 localPosition)? GetStaticObjectTransform(ushort landblockId, ObjectId instanceId) => _gameScene?.GetStaticObjectTransform(landblockId, instanceId);
    public uint GetEnvCellAt(Vector3 worldPos) => _gameScene?.GetEnvCellAt(worldPos) ?? 0;

    private void RequestCommitObjectChange(ISelectedObjectInfo info) {
        if (_project.IsReadOnly) return;
        _commitDebounce.Request(info.InstanceId, () => CommitObjectChange(info));
    }

    private void CommitObjectChange(ISelectedObjectInfo info) {
        var activeDoc = ActiveDocument;
        if (_toolContext == null || activeDoc == null) return;

        // Resolve layerId
        var layerId = _landscapeObjectService.GetStaticObjectLayerId(activeDoc, info.LandblockId, info.InstanceId);
        if (string.IsNullOrEmpty(layerId)) {
            layerId = ActiveLayer?.Id ?? activeDoc.BaseLayerId ?? "";
        }

        // Get old object for undo
        _ = Task.Run(async () => {
            if (activeDoc.Region == null) return;

            // Determine the final cell assignment logically (async)
            uint? finalCellId = GetEnvCellAt(info.Position);
            if (finalCellId == 0) finalCellId = null;
            
            // Determine if the object crossed landblock boundaries or moved into/out of a cell
            ushort newLandblockId = _landscapeObjectService.ComputeLandblockId(activeDoc.Region, info.Position);

            // Recalculate local position relative to the NEW landblock/cell origin
            var lbOrigin = _landscapeObjectService.ComputeWorldPosition(activeDoc.Region, newLandblockId, Vector3.Zero);
            var newLocalPosition = info.Position - lbOrigin;

            StaticObject? oldObject = null;
            var type = info.InstanceId.Type;
            if (type == ObjectType.EnvCellStaticObject) {
                var cellId = info.InstanceId.Context;
                oldObject = (await activeDoc.GetMergedEnvCellAsync(cellId)).StaticObjects.GetValueOrDefault(info.InstanceId);
            }
            else {
                var lb = await activeDoc.GetMergedLandblockAsync(info.LandblockId);
                if (type == ObjectType.Building) {
                    var building = lb.Buildings.GetValueOrDefault(info.InstanceId);
                    if (building != null) {
                        oldObject = new StaticObject {
                            InstanceId = building.InstanceId,
                            ModelId = building.ModelId,
                            LayerId = building.LayerId,
                            Position = building.Position,
                            Rotation = building.Rotation
                        };
                    }
                }
                else {
                    oldObject = lb.StaticObjects.GetValueOrDefault(info.InstanceId);
                }
            }

            if (oldObject == null) return;

            var newObject = new StaticObject {
                ModelId = info.ObjectId,
                InstanceId = info.InstanceId,
                LayerId = layerId,
                Position = newLocalPosition,
                Rotation = info.Rotation,
                CellId = finalCellId
            };

            var command = new MoveStaticObjectCommand(
                activeDoc,
                _toolContext,
                layerId,
                info.LandblockId,
                newLandblockId,
                oldObject,
                newObject);

            await Dispatcher.UIThread.InvokeAsync(() => {
                _isUpdatingFromSelection = true;
                try {
                    CommandHistory.Execute(command);
                }
                finally {
                    _isUpdatingFromSelection = false;
                }
            });
        });
    }

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ushort, byte>> _dirtyChunks = new();

    public void RequestSave(string docId, IEnumerable<ushort>? affectedChunks = null) {
        if (_project.IsReadOnly) return;

        if (affectedChunks != null) {
            var dirty = _dirtyChunks.GetOrAdd(docId, _ => new ConcurrentDictionary<ushort, byte>());
            foreach (var chunkId in affectedChunks) {
                dirty.TryAdd(chunkId, 0);
            }
        }

        _saveDebounce.Request(docId, async () => {
            await Dispatcher.UIThread.InvokeAsync(() => PersistDocumentAsync(docId, default));
        });
    }

    private async Task PersistDocumentAsync(string docId, CancellationToken ct) {
        if (_project.IsReadOnly || ActiveDocument == null) return;

        if (docId == ActiveDocument.Id) {
            _log.LogDebug("Persisting landscape document {DocId} to database", docId);
            await _documentManager.PersistDocumentAsync(_landscapeRental!, null!, ct);

            // Persist dirty chunks
            if (_dirtyChunks.TryRemove(docId, out var dirtyChunks)) {
                foreach (var chunkId in dirtyChunks.Keys) {
                    if (ActiveDocument.LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                        if (chunk.EditsRental != null) {
                            _log.LogTrace("Persisting chunk {ChunkId} for document {DocId}", chunkId, docId);
                            await _documentManager.PersistDocumentAsync(chunk.EditsRental, null!, ct);
                        }
                        else if (chunk.EditsDetached != null) {
                            _log.LogTrace("Creating chunk document {ChunkId} for document {DocId}", chunkId, docId);
                            var createResult = await _documentManager.CreateDocumentAsync(chunk.EditsDetached, null!, ct);
                            if (createResult.IsSuccess) {
                                chunk.EditsRental = createResult.Value;
                                chunk.EditsDetached = null;
                            }
                            else {
                                _log.LogError("Failed to create chunk document: {Error}", createResult.Error);
                            }
                        }
                    }
                }
            }
            return;
        }

        _log.LogWarning("PersistDocumentAsync called with unknown ID {DocId}, saving main document instead", docId);
        await _documentManager.PersistDocumentAsync(_landscapeRental!, null!, ct);
    }

    public void InitializeToolContext(ICamera camera, Action<int, int> invalidateCallback) {
        Camera = camera;
        _invalidateCallback = (x, y) => {
            if (ActiveDocument != null) {
                if (x == -1 && y == -1) {
                    ActiveDocument.NotifyLandblockChanged(null, LandblockChangeType.All);
                }
                else {
                    ActiveDocument.NotifyLandblockChanged(new[] { (x, y) }, LandblockChangeType.All);
                }
            }
            invalidateCallback(x, y);
        };
        UpdateToolContext();
    }

    private GameScene? _gameScene;

    public bool IsDebugShapesEnabled {
        get => EditorState.ShowDebugShapes;
        set => EditorState.ShowDebugShapes = value;
    }

    public void SetGameScene(GameScene scene) {
        if (_gameScene != null) {
            _gameScene.OnPointerPressed -= OnPointerPressed;
            _gameScene.OnPointerMoved -= OnPointerMoved;
            _gameScene.OnPointerReleased -= OnPointerReleased;
            _gameScene.OnCameraChanged -= OnCameraChanged;
            _gameScene.Camera2D.OnChanged -= OnCameraStateChanged;
            _gameScene.Camera3D.OnChanged -= OnCameraStateChanged;
        }

        _gameScene = scene;

        if (_gameScene != null) {
            _gameScene.State = EditorState;
            _gameScene.OnPointerPressed += OnPointerPressed;
            _gameScene.OnPointerMoved += OnPointerMoved;
            _gameScene.OnPointerReleased += OnPointerReleased;
            _gameScene.OnCameraChanged += OnCameraChanged;
            _gameScene.Camera2D.OnChanged += OnCameraStateChanged;
            _gameScene.Camera3D.OnChanged += OnCameraStateChanged;

            _gameScene.SetInspectorTool(ActiveTool as InspectorTool);
            _gameScene.SetManipulationTool(ActiveTool as ObjectManipulationTool);

            RestoreCameraState();
        }
    }

    private bool _isRestoringCamera;

    private void RestoreCameraState() {
        if (_settings?.Project == null || _gameScene == null) return;

        _log.LogTrace("Restoring camera state from project settings");
        _isRestoringCamera = true;
        try {
            var projectSettings = _settings.Project;

            _gameScene.RestoreCamera(projectSettings.LandscapeCameraLocationString, projectSettings.LandscapeCameraFieldOfView);
            _gameScene.Camera3D.MoveSpeed = projectSettings.LandscapeCameraMovementSpeed;
            _gameScene.SetCameraMode(projectSettings.LandscapeCameraIs3D);
        }
        finally {
            _isRestoringCamera = false;
        }
    }

    private void OnCameraStateChanged() {
        if (_isRestoringCamera || _settings?.Project == null || _gameScene == null) return;

        var projectSettings = _settings.Project;
        projectSettings.LandscapeCameraLocationString = _gameScene.GetLocationString();

        projectSettings.LandscapeCameraIs3D = _gameScene.Is3DMode;
        projectSettings.LandscapeCameraMovementSpeed = _gameScene.Camera3D.MoveSpeed;
        projectSettings.LandscapeCameraFieldOfView = (int)_gameScene.Camera3D.FieldOfView;
    }


    private void OnCameraChanged(bool is3d) {
        Dispatcher.UIThread.Post(() => {
            Is3DCameraEnabled = is3d;
            UpdateToolContext();
            OnCameraStateChanged();
        });
    }

    public void OnPointerPressed(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }

        if (ActiveTool != null && _toolContext != null) {
            if (ActiveTool.OnPointerPressed(e)) {
                // Handled
            }
        }
    }

    public void OnPointerMoved(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }

        if (e.IsRightDown) return;

        ActiveTool?.OnPointerMoved(e);
    }

    public void OnPointerReleased(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }
        ActiveTool?.OnPointerReleased(e);
    }

    private async Task LoadLandscapeAsync() {
        try {
            var regionId = _dats.CellRegions.Keys.OrderBy(k => k).FirstOrDefault();
            var rental = await _project.Landscape.GetOrCreateTerrainDocumentAsync(regionId, CancellationToken.None);

            await Dispatcher.UIThread.InvokeAsync(() => {
                _landscapeRental = rental;
                ActiveDocument = _landscapeRental.Document;
            });

            // Auto-connect whenever a host is configured; warns in log on failure and keeps retrying.
            var aceWorld = _settings.AceWorld;
            if (aceWorld != null && !string.IsNullOrWhiteSpace(aceWorld.Host)) {
                _ = Task.Run(() => LoadAceEncountersAsync(CancellationToken.None));
            }
        }
        catch (Exception ex) {
            _log.LogError(ex, "Error loading landscape");
        }
    }

    // In-memory encounter dots shown on the minimap — no document writes, no undo history.
    [ObservableProperty]
    private IReadOnlyList<EncounterMapDot>? _encounterMapDots;

    [RelayCommand]
    private async Task LoadAceEncountersAsync(CancellationToken ct = default) {
        if (ActiveDocument == null) return;
        await LoadAceEncountersInternalAsync(ct); // lock is inside; skips if already running
    }

    /// <summary>
    /// Computes the set of landblock IDs that should be loaded around the current camera
    /// using Manhattan distance so the shape is a diamond, not a circle or square.
    /// </summary>
    private HashSet<ushort> ComputeDesiredLandblockIds(ITerrainInfo region) {
        var cameraPos  = Camera?.Position ?? Vector3.Zero;
        var centerLbId = _landscapeObjectService.ComputeLandblockId(region, cameraPos);
        int cX = (centerLbId >> 8) & 0xFF;
        int cY =  centerLbId       & 0xFF;
        const int radius = 2; // Manhattan distance — 13-LB diamond
        var result = new HashSet<ushort>();
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dy = -radius; dy <= radius; dy++) {
                if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
                int lbX = cX + dx, lbY = cY + dy;
                if (lbX is >= 0 and <= 254 && lbY is >= 0 and <= 254)
                    result.Add((ushort)((lbX << 8) | lbY));
            }
        }
        return result;
    }

    /// <summary>
    /// Incremental load: computes desired vs loaded landblock sets, queries only the diff,
    /// clears only the departed ones. Never does a full clear — prevents creatures vanishing.
    /// Guarded by a SemaphoreSlim so the tick and the manual command can't overlap.
    /// </summary>
    private async Task LoadAceEncountersInternalAsync(CancellationToken ct) {
        if (!_encounterLoadLock.Wait(0)) return; // already running
        try {
            var region = ActiveDocument?.Region;
            if (region == null) return;

            var aceSettings = _settings.AceWorld?.ToAceDbSettings();
            if (aceSettings == null || string.IsNullOrWhiteSpace(aceSettings.Host)) return;

            var desiredLbs = ComputeDesiredLandblockIds(region);
            var toLoad   = desiredLbs.Where(id => !_loadedEncounterLbIds.Contains(id)).ToList();
            var toRemove = _loadedEncounterLbIds.Where(id => !desiredLbs.Contains(id)).ToList();

            if (toLoad.Count == 0 && toRemove.Count == 0) return; // camera hasn't left current set

            // --- Remove departed landblocks (no DB needed) ---
            if (_gameScene != null) {
                foreach (var lbId in toRemove) {
                    _gameScene.ClearEncounterOverlay(lbId);
                    _gameScene.InvalidateEncounterLandblock(lbId);
                    _encounterDotsByLb.Remove(lbId);
                }
            }
            foreach (var lbId in toRemove) _loadedEncounterLbIds.Remove(lbId);

            // --- Load new landblocks ---
            if (toLoad.Count > 0) {
                // Connect once we know there's actual work to do
                var connector  = new AceDbConnector(aceSettings);
                var connError  = await connector.TestConnectionAsync(ct);
                if (connError != null) {
                    _log.LogWarning("[ACE Encounters] Cannot connect to {Host}: {Err}", aceSettings.Host, connError);
                    // Don't mark toLoad as loaded — will retry next tick
                    goto rebuildDots;
                }

                // Pre-mark as loaded so concurrent ticks don't double-query
                foreach (var id in toLoad) _loadedEncounterLbIds.Add(id);

                var spawns = await connector.GetEncounterSpawnsForAreaAsync(
                    toLoad.Select(id => (int)id).ToList(), ct);

                // Group by landblock
                var byLb = new Dictionary<ushort, (List<EncounterMapDot> Dots, List<(ObjectId InstId, uint SetupDid, Vector3 XyPos, float Scale)> Entries)>();
                for (int i = 0; i < spawns.Count; i++) {
                    var spawn = spawns[i];
                    var lbId  = (ushort)spawn.Landblock;
                    var localX = Math.Clamp(spawn.CellX * 24f, 0.5f, 191.5f);
                    var localY = Math.Clamp(spawn.CellY * 24f, 0.5f, 191.5f);
                    var xyPos  = _landscapeObjectService.ComputeWorldPosition(region, lbId, new Vector3(localX, localY, 0f));

                    if (!byLb.TryGetValue(lbId, out var lbData))
                        byLb[lbId] = lbData = (new List<EncounterMapDot>(), new List<(ObjectId, uint, Vector3, float)>());

                    lbData.Dots.Add(new EncounterMapDot(xyPos.X, xyPos.Y, IsGenerator: false, spawn.SpawnName));

                    if (spawn.SetupDid == 0 || _gameScene == null) continue;
                    var hash   = (uint)HashCode.Combine(spawn.Landblock, spawn.CellX, spawn.CellY);
                    var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, hash, (ushort)(i & 0xFFFF));
                    lbData.Entries.Add((instId, spawn.SetupDid, xyPos, spawn.Scale));
                }

                // Push to renderer
                if (_gameScene != null) {
                    var scene = _gameScene;
                    foreach (var kvp in byLb) {
                        var lbId    = kvp.Key;
                        var entries = kvp.Value.Entries.Select(s => {
                            var z = scene.GetTerrainHeight(s.XyPos.X, s.XyPos.Y);
                            return (s.InstId, s.SetupDid, new Vector3(s.XyPos.X, s.XyPos.Y, z), Quaternion.Identity, s.Scale);
                        }).ToList();
                        scene.SetEncounterOverlay(lbId, entries);
                        scene.InvalidateEncounterLandblock(lbId);
                        _encounterDotsByLb[lbId] = kvp.Value.Dots;
                    }
                }

                _log.LogInformation("[ACE Encounters] +{Load} LBs / -{Remove} LBs | {Spawns} spawns",
                    toLoad.Count, toRemove.Count, spawns.Count);
            }

            rebuildDots:
            var allDots = _encounterDotsByLb.Values.SelectMany(d => d).ToList();
            await Dispatcher.UIThread.InvokeAsync(() => EncounterMapDots = allDots);

            EnsureEncounterTickRunning();
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) {
            _log.LogDebug(ex, "[ACE Encounters] Tick error");
        }
        finally {
            _encounterLoadLock.Release();
        }
    }

    private void EnsureEncounterTickRunning() {
        if (_encounterTickCts != null) return;
        _encounterTickCts = new CancellationTokenSource();
        var token = _encounterTickCts.Token;
        _ = Task.Run(async () => {
            while (!token.IsCancellationRequested) {
                try {
                    await Task.Delay(3_000, token); // 3-second tick
                    await LoadAceEncountersInternalAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch { /* keep ticking */ }
            }
        }, token);
    }


    [RelayCommand]
    private async Task SpawnEncounterAtCameraAsync() {
        if (IsReadOnly || ActiveDocument == null || Camera == null) {
            _log.LogWarning("[Encounter Spawn] Cannot spawn: project is read-only or document/camera not ready");
            return;
        }

        try {
            var aceSettings = _settings.AceWorld.ToAceDbSettings();
            var connector = new AceDbConnector(aceSettings);

            // Step 1 – first encounter row
            var encounters = await connector.GetEncountersAsync(1, CancellationToken.None);
            if (encounters.Count == 0) {
                _log.LogWarning("[Encounter Spawn] No encounters found in '{Db}'", aceSettings.Database);
                return;
            }
            var enc = encounters[0];

            // Step 2 – first generator row → actual spawned mob id
            var generators = await connector.GetWeenieGeneratorsAsync(enc.WeenieClassId, 1, CancellationToken.None);
            uint spawnId = generators.Count > 0 ? generators[0].SpawnWeenieClassId : enc.WeenieClassId;
            var spawnName = generators.Count > 0 ? generators[0].SpawnWeenieName : enc.WeenieName;

            // Step 3 – Setup DID for the spawned mob
            var setupDid = await connector.GetWeenieSetupDidAsync(spawnId, CancellationToken.None);
            if (setupDid == 0)
                setupDid = await connector.GetWeenieSetupDidAsync(enc.WeenieClassId, CancellationToken.None);

            if (setupDid == 0) {
                _log.LogWarning("[Encounter Spawn] No Setup DID found for weenie {Id} ({Name})", spawnId, spawnName);
                return;
            }

            // Step 4 – world position 5 units in front of the camera
            var worldPos = Camera.Position + Camera.Forward * 5f;

            // Step 5 – landblock + local-space position
            var region = ActiveDocument.Region;
            if (region == null) {
                _log.LogWarning("[Encounter Spawn] Region not loaded");
                return;
            }
            var landblockId = _landscapeObjectService.ComputeLandblockId(region, worldPos);
            var lbOrigin    = _landscapeObjectService.ComputeWorldPosition(region, landblockId, Vector3.Zero);
            var localPos    = worldPos - lbOrigin;

            uint? cellId = _gameScene?.GetEnvCellAt(worldPos);
            if (cellId == 0) cellId = null;

            var objType    = cellId.HasValue ? ObjectType.EnvCellStaticObject : ObjectType.StaticObject;
            var instanceId = InstanceIdGenerator.GenerateUniqueInstanceId(ActiveDocument, landblockId, cellId, objType);
            var layerId    = ActiveLayer?.Id ?? ActiveDocument.BaseLayerId ?? "";

            var spawnObj = new StaticObject {
                ModelId    = setupDid,
                InstanceId = instanceId,
                LayerId    = layerId,
                Position   = localPos,
                Rotation   = Quaternion.Identity,
                CellId     = cellId
            };

            AddStaticObject(layerId, landblockId, spawnObj);
            _log.LogInformation("[Encounter Spawn] Placed 0x{Setup:X8} ({Name}) at {Pos}", setupDid, spawnName, worldPos);
        }
        catch (Exception ex) {
            _log.LogError(ex, "[Encounter Spawn] Failed");
        }
    }

    [RelayCommand]
    public void ResetCamera() {
        if (_gameScene?.CurrentCamera is Camera3D cam3d) {
            cam3d.Yaw = 0;
            cam3d.Pitch = 0;
        }

        // teleport to Yaraq (both 2D and 3D) 
        if (Position.TryParse("21.6S, 1.8W", out var pos, ActiveDocument?.Region)) {
            uint cellId = (uint)((pos!.LandblockId << 16) | pos.CellId);
            _gameScene?.Teleport(pos.GlobalPosition, cellId);
        }
    }

    private void SyncSettingsToState() {
        if (_settings == null) return;
        EditorState.ShowScenery = _settings.Landscape.Rendering.ShowScenery;
        EditorState.ShowStaticObjects = _settings.Landscape.Rendering.ShowStaticObjects;
        EditorState.ShowBuildings = _settings.Landscape.Rendering.ShowBuildings;
        EditorState.ShowEnvCells = _settings.Landscape.Rendering.ShowEnvCells;
        EditorState.ShowParticles = _settings.Landscape.Rendering.ShowParticles;
        EditorState.ShowSkybox = _settings.Landscape.Rendering.ShowSkybox;
        EditorState.ShowUnwalkableSlopes = _settings.Landscape.Rendering.ShowUnwalkableSlopes;
        EditorState.ShowDisqualifiedScenery = _settings.Landscape.Rendering.ShowDisqualifiedScenery;
        EditorState.ObjectRenderDistance = _settings.Landscape.Rendering.ObjectRenderDistance;
        EditorState.MaxDrawDistance = _settings.Landscape.Camera.MaxDrawDistance;
        EditorState.MouseSensitivity = _settings.Landscape.Camera.MouseSensitivity;
        EditorState.AltMouseLook = _settings.Landscape.Camera.AltMouseLook;
        EditorState.EnableCameraCollision = _settings.Landscape.Camera.EnableCameraCollision;
        EditorState.EnableTransparencyPass = _settings.Landscape.Rendering.EnableTransparencyPass;
        EditorState.TimeOfDay = _settings.Landscape.Rendering.TimeOfDay;
        EditorState.LightIntensity = _settings.Landscape.Rendering.LightIntensity;

        EditorState.ShowGrid = _settings.Landscape.Grid.ShowGrid;
        EditorState.ShowLandblockGrid = true;
        EditorState.ShowCellGrid = true;
        EditorState.LandblockGridColor = _settings.Landscape.Grid.LandblockColor;
        EditorState.CellGridColor = _settings.Landscape.Grid.CellColor;
        EditorState.GridLineWidth = _settings.Landscape.Grid.LineWidth;
        EditorState.GridOpacity = _settings.Landscape.Grid.Opacity;

        if (_settings.Project != null) {
            Is3DCameraEnabled = _settings.Project.LandscapeCameraIs3D;
        }
    }

    private void OnEditorStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_settings == null) return;
        switch (e.PropertyName) {
            case nameof(EditorState.ShowScenery): _settings.Landscape.Rendering.ShowScenery = EditorState.ShowScenery; break;
            case nameof(EditorState.ShowDisqualifiedScenery): _settings.Landscape.Rendering.ShowDisqualifiedScenery = EditorState.ShowDisqualifiedScenery; break;
            case nameof(EditorState.ShowStaticObjects): _settings.Landscape.Rendering.ShowStaticObjects = EditorState.ShowStaticObjects; break;
            case nameof(EditorState.ShowBuildings): _settings.Landscape.Rendering.ShowBuildings = EditorState.ShowBuildings; break;
            case nameof(EditorState.ShowEnvCells): _settings.Landscape.Rendering.ShowEnvCells = EditorState.ShowEnvCells; break;
            case nameof(EditorState.ShowParticles): _settings.Landscape.Rendering.ShowParticles = EditorState.ShowParticles; break;
            case nameof(EditorState.ShowSkybox): _settings.Landscape.Rendering.ShowSkybox = EditorState.ShowSkybox; break;
            case nameof(EditorState.ShowUnwalkableSlopes): _settings.Landscape.Rendering.ShowUnwalkableSlopes = EditorState.ShowUnwalkableSlopes; break;
            case nameof(EditorState.ObjectRenderDistance): _settings.Landscape.Rendering.ObjectRenderDistance = EditorState.ObjectRenderDistance; break;
            case nameof(EditorState.MaxDrawDistance): _settings.Landscape.Camera.MaxDrawDistance = EditorState.MaxDrawDistance; break;
            case nameof(EditorState.MouseSensitivity): _settings.Landscape.Camera.MouseSensitivity = EditorState.MouseSensitivity; break;
            case nameof(EditorState.AltMouseLook): _settings.Landscape.Camera.AltMouseLook = EditorState.AltMouseLook; break;
            case nameof(EditorState.EnableCameraCollision): _settings.Landscape.Camera.EnableCameraCollision = EditorState.EnableCameraCollision; break;
            case nameof(EditorState.EnableTransparencyPass): _settings.Landscape.Rendering.EnableTransparencyPass = EditorState.EnableTransparencyPass; break;
            case nameof(EditorState.TimeOfDay): _settings.Landscape.Rendering.TimeOfDay = EditorState.TimeOfDay; break;
            case nameof(EditorState.LightIntensity): _settings.Landscape.Rendering.LightIntensity = EditorState.LightIntensity; break;
            case nameof(EditorState.ShowGrid): _settings.Landscape.Grid.ShowGrid = EditorState.ShowGrid; break;
            case nameof(EditorState.LandblockGridColor): _settings.Landscape.Grid.LandblockColor = EditorState.LandblockGridColor; break;
            case nameof(EditorState.CellGridColor): _settings.Landscape.Grid.CellColor = EditorState.CellGridColor; break;
            case nameof(EditorState.GridLineWidth): _settings.Landscape.Grid.LineWidth = EditorState.GridLineWidth; break;
            case nameof(EditorState.GridOpacity): _settings.Landscape.Grid.Opacity = EditorState.GridOpacity; break;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(WorldBuilderSettings.Landscape)) {
            SyncSettingsToState();
        }
    }

    private void OnLandscapeSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LandscapeEditorSettings.Rendering) ||
            e.PropertyName == nameof(LandscapeEditorSettings.Grid) ||
            e.PropertyName == nameof(LandscapeEditorSettings.Camera)) {
            SyncSettingsToState();
        }
    }

    private void OnCameraSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        SyncSettingsToState();
    }

    private void OnRenderingSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        SyncSettingsToState();
    }

    private void OnGridSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        SyncSettingsToState();
    }

    private void CancelPendingCommits() {
        _commitDebounce.CancelAll();
    }

    private void OnCommandHistoryChanged(object? sender, CommandHistoryChangedEventArgs e) {
        if (e.ChangeType == CommandChangeType.Undo || e.ChangeType == CommandChangeType.Redo || e.ChangeType == CommandChangeType.Clear) {
            CancelPendingCommits();
        }
    }

    public bool HandleHotkey(KeyEventArgs e) {
        var inputManager = _inputManager as InputManager;
        if (e.Key == Key.Escape) {
            if (ActiveTool is ITexturePaintingTool paintingTool && paintingTool.IsEyeDropperActive) {
                paintingTool.IsEyeDropperActive = false;
                return true;
            }
        }
        if (inputManager != null && inputManager.IsAction(e, InputAction.GoToLocation)) {
            _ = ShowGoToLocationPrompt();
            return true;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z) {
            _commitDebounce.CancelAll();
            CommandHistory.Undo();
            return true;
        }
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z) {
            _commitDebounce.CancelAll();
            CommandHistory.Redo();
            return true;
        }
        if (inputManager != null && inputManager.IsAction(e, InputAction.AddBookmark)) { 
            BookmarksPanel?.AddBookmark();
            return true;
        }
        return false;
    }

    private async Task ShowGoToLocationPrompt() {
        var vm = _dialogService.CreateViewModel<TextInputWindowViewModel>();
        vm.Title = "Go To Location";
        vm.Message = "Enter location (e.g. 12.3N, 45.6E or 0x12340001 [0 0 0]):";

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as INotifyPropertyChanged;
        if (owner != null) {
            await _dialogService.ShowDialogAsync(owner, vm);
        }
        else {
            await _dialogService.ShowDialogAsync(null!, vm);
        }

        if (vm.Result && Position.TryParse(vm.InputText, out var pos, ActiveDocument?.Region)) {
            uint cellId = (uint)((pos!.LandblockId << 16) | pos.CellId);
            _gameScene?.Teleport(pos.GlobalPosition, cellId);
        }
    }

    public void OnActiveTabChanged(string tabName) {
        if (_settings?.Project != null && !string.IsNullOrEmpty(tabName)) {
            _settings.Project.ActiveTab = tabName;
        }
    }

    public void Dispose() {
        _encounterTickCts?.Cancel();
        _encounterTickCts?.Dispose();
        _encounterTickCts = null;
        _encounterLoadLock.Dispose();
        _settingsBridge.Dispose();
        CommandHistory.OnChange -= OnCommandHistoryChanged;
        _landscapeRental?.Dispose();
    }
}

/// <summary>
/// Lightweight data point for minimap rendering. WorldX/Y are in world-space units.
/// IsGenerator = true → yellow dot (generator weenie), false → orange dot (spawned mob position).
/// </summary>
public record EncounterMapDot(float WorldX, float WorldY, bool IsGenerator, string Name);