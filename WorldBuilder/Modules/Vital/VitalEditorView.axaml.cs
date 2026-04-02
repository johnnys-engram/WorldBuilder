using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WorldBuilder.Modules.Vital;

public partial class VitalEditorView : UserControl {
    public VitalEditorView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is VitalEditorViewModel vm) {
            await vm.InitializeAsync();
        }
    }
}
