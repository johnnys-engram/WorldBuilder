using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WorldBuilder.Modules.ObjectDebug;

public partial class ObjectDebugView : UserControl {
    public ObjectDebugView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is ObjectDebugViewModel vm)
            await vm.InitializeAsync();
    }
}
