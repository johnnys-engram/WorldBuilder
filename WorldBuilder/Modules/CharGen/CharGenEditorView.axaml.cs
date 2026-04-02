using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WorldBuilder.Modules.CharGen;

public partial class CharGenEditorView : UserControl {
    public CharGenEditorView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is CharGenEditorViewModel vm)
            await vm.InitializeAsync();
    }
}
