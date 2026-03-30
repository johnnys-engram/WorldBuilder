using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Spell;

public class SpellEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Spell Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<SpellEditorViewModel>();

    public SpellEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
