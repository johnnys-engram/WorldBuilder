using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Skill;

public class SkillEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Skill Editor";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<SkillEditorViewModel>();

    public SkillEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
