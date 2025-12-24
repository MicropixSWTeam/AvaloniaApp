using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls;

public partial class ProcessView : UserControl
{
    private ProcessViewModel? ViewModel => DataContext as ProcessViewModel;

    public ProcessView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 화면에 나타날 때 이벤트 구독
        if (ViewModel != null)
        {
            ViewModel.CameraVM.ProcessedPreviewInvalidated += InvalidateProcessedImage;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 화면에서 사라질 때 이벤트 구독 해제
        if (ViewModel != null)
        {
            ViewModel.CameraVM.ProcessedPreviewInvalidated -= InvalidateProcessedImage;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
    }

    // 이 메서드가 호출되면 이미지를 다시 그립니다.
    private void InvalidateProcessedImage()
    {
        ProcessedImage?.InvalidateVisual();
    }
}