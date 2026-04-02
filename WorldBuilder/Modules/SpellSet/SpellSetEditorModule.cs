using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.SpellSet;

public class SpellSetEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Spell Set Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<SpellSetEditorViewModel>();

    public SpellSetEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
