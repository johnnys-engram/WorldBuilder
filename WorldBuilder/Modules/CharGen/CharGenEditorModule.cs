using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.CharGen;

public class CharGenEditorModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "CharGen";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<CharGenEditorViewModel>();

    public CharGenEditorModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
