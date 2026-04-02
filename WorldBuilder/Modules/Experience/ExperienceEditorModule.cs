using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Experience;

public class ExperienceEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Experience Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<ExperienceEditorViewModel>();

    public ExperienceEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
