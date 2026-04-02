using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Layout;

public class LayoutEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Layout Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<LayoutEditorViewModel>();

    public LayoutEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
