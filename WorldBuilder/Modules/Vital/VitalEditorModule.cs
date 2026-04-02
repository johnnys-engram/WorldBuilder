using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Vital;

public class VitalEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Vital Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<VitalEditorViewModel>();

    public VitalEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
