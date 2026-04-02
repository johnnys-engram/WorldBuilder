using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WorldBuilder.Modules.Experience;

public partial class ExperienceEditorView : UserControl {
    public ExperienceEditorView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is ExperienceEditorViewModel vm) {
            await vm.InitializeAsync();
        }
    }
}
