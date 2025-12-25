using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaEdit.Utils;
using System;
using System.Linq;

namespace AvaloniaApp.Presentation.Views.UserControls;

public partial class RawImageView : UserControl
{
    private bool _isDragging;
    private Point _startPoint;

    private RawImageViewModel? ViewModel => DataContext as RawImageViewModel;
    public RawImageView()
    {
        InitializeComponent();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.RawPreviewInvalidated += InvalidatePreviewImage;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.RawPreviewInvalidated -= InvalidatePreviewImage;
    }
    private void InvalidatePreviewImage() => PreviewImage?.InvalidateVisual();
}