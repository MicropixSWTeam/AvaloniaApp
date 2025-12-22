using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls;

public partial class RgbImageView : UserControl
{
    private RgbImageViewModel? ViewModel => DataContext as RgbImageViewModel;

    public RgbImageView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.RgbPreviewInvalidated += InvalidateRgbImage;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.RgbPreviewInvalidated -= InvalidateRgbImage;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // DataContext가 변경되었을 때 이벤트 재구독 처리 (필요시)
        // 보통은 Attached/Detached에서 처리하면 충분함
    }

    private void InvalidateRgbImage()
    {
        RgbImage?.InvalidateVisual();
    }
}