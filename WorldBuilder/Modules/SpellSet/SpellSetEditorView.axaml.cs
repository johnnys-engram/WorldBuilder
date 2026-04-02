using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WorldBuilder.Modules.SpellSet;

public partial class SpellSetEditorView : UserControl {
    public SpellSetEditorView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is SpellSetEditorViewModel vm) {
            await vm.InitializeAsync();
        }
    }
}
