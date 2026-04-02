using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Weenie;

public class WeenieEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Weenie Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<WeenieEditorViewModel>();

    public WeenieEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
