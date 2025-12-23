using Avalonia;
using Avalonia.Controls;
using AvaloniaApp.Presentation.ViewModels.UserControls;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class ProcessView : UserControl
    {
        private ProcessViewModel? ViewModel => DataContext as ProcessViewModel;
        public ProcessView() => InitializeComponent();

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (ViewModel != null) ViewModel.CameraVM.ProcessedPreviewInvalidated += InvalidateProcessedImage;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (ViewModel != null) ViewModel.CameraVM.ProcessedPreviewInvalidated -= InvalidateProcessedImage;
        }

        private void InvalidateProcessedImage() => ProcessedImage?.InvalidateVisual();
    }
}