using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.ComponentModel;

namespace WorldBuilder.Modules.Layout;

public partial class LayoutEditorView : UserControl {
    private LayoutEditorViewModel? _viewModel;
    private LayoutPreviewCanvas? _previewCanvas;
    private LayoutDetailViewModel? _detailSubscribed;
    private ElementTreeNode? _elementSubscribed;
    private bool _wired;

    public LayoutEditorView() {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (Design.IsDesignMode) return;
        if (DataContext is not LayoutEditorViewModel vm) return;
        if (_wired) return;
        _wired = true;

        _viewModel = vm;
        await vm.InitializeAsync();

        _previewCanvas = this.FindControl<LayoutPreviewCanvas>("PreviewCanvas");

        vm.PropertyChanged += OnViewModelPropertyChanged;
        AttachDetail(vm.SelectedDetail);
        UpdatePreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(LayoutEditorViewModel.SelectedDetail)) return;
        DetachDetail();
        AttachDetail(_viewModel?.SelectedDetail);
        UpdatePreview();
    }

    void AttachDetail(LayoutDetailViewModel? detail) {
        _detailSubscribed = detail;
        if (detail == null) return;
        detail.PropertyChanged += OnDetailPropertyChanged;
        AttachElement(detail.SelectedElement);
    }

    void DetachDetail() {
        DetachElement();
        if (_detailSubscribed != null) {
            _detailSubscribed.PropertyChanged -= OnDetailPropertyChanged;
            _detailSubscribed = null;
        }
    }

    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LayoutDetailViewModel.SelectedElement)) {
            DetachElement();
            AttachElement(_detailSubscribed?.SelectedElement);
            UpdatePreview();
            return;
        }

        if (e.PropertyName == nameof(LayoutDetailViewModel.ElementTextures) ||
            e.PropertyName == nameof(LayoutDetailViewModel.Width) ||
            e.PropertyName == nameof(LayoutDetailViewModel.Height)) {
            UpdatePreview();
        }
    }

    void AttachElement(ElementTreeNode? node) {
        _elementSubscribed = node;
        if (node != null)
            node.PropertyChanged += OnSelectedElementPropertyChanged;
    }

    void DetachElement() {
        if (_elementSubscribed != null) {
            _elementSubscribed.PropertyChanged -= OnSelectedElementPropertyChanged;
            _elementSubscribed = null;
        }
    }

    void OnSelectedElementPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        UpdatePreview();
        if (sender is not ElementTreeNode node) return;
        switch (e.PropertyName) {
            case nameof(ElementTreeNode.X):
            case nameof(ElementTreeNode.Y):
            case nameof(ElementTreeNode.Width):
            case nameof(ElementTreeNode.Height):
            case nameof(ElementTreeNode.ZLevel):
            case nameof(ElementTreeNode.LeftEdge):
            case nameof(ElementTreeNode.TopEdge):
            case nameof(ElementTreeNode.RightEdge):
            case nameof(ElementTreeNode.BottomEdge):
            case nameof(ElementTreeNode.ReadOrder):
                SyncGeomTextsFromElement(node);
                break;
        }
    }

    void SyncGeomTextsFromElement(ElementTreeNode node) {
        var detail = _viewModel?.SelectedDetail;
        if (detail == null || node != detail.SelectedElement) return;
        detail.ElementGeomXText = node.X.ToString();
        detail.ElementGeomYText = node.Y.ToString();
        detail.ElementGeomWidthText = node.Width.ToString();
        detail.ElementGeomHeightText = node.Height.ToString();
        detail.ElementGeomZText = node.ZLevel.ToString();
        detail.ElementGeomLeftText = node.LeftEdge.ToString();
        detail.ElementGeomTopText = node.TopEdge.ToString();
        detail.ElementGeomRightText = node.RightEdge.ToString();
        detail.ElementGeomBottomText = node.BottomEdge.ToString();
        detail.ElementReadOrderText = node.ReadOrder.ToString();
    }

    private void UpdatePreview() {
        if (_previewCanvas == null) return;
        _previewCanvas.SetLayout(
            _viewModel?.SelectedDetail?.RootElements,
            _viewModel?.SelectedDetail?.Width ?? 0,
            _viewModel?.SelectedDetail?.Height ?? 0,
            _viewModel?.SelectedDetail?.SelectedElement,
            _viewModel?.SelectedDetail?.ElementTextures);
    }
}
