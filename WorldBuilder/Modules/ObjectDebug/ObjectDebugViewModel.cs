using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.ObjectDebug;

/// <summary>
/// Placeholder shell for the ACME Object Debug editor. Full GL preview requires StaticObjectManager
/// and landscape/ObjectDebug integration (see plan Phase E).
/// </summary>
public partial class ObjectDebugViewModel : ViewModelBase {
    private readonly Project _project;
    private readonly IDatReaderWriter _dats;
    private bool _initialized;

    [ObservableProperty] private string _statusText =
        "Object Debug: GL preview and setup browsing are not wired in this build.";

    public ObjectDebugViewModel(Project project, IDatReaderWriter dats) {
        _project = project;
        _dats = dats;
    }

    public Task InitializeAsync(CancellationToken ct = default) {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        StatusText = _project.IsReadOnly
            ? "Read-only project — object debug editing is disabled."
            : $"Object Debug placeholder. Portal DAT has {_dats.Portal.GetAllIdsOfType<Setup>().Count()} setup ids (preview not implemented).";
        return Task.CompletedTask;
    }
}
