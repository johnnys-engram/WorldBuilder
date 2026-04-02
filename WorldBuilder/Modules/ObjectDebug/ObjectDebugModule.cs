using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.ObjectDebug;

public class ObjectDebugModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Object Debug";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<ObjectDebugViewModel>();

    public ObjectDebugModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
