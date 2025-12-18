using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaEdit.Utils;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class CameraView : UserControl
    {
        private bool _isDragging;
        private Point _start;

        private CameraViewModel? Vm => DataContext as CameraViewModel;

        public CameraView()
        {
            InitializeComponent();
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }
        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (Vm is not null)
                Vm.PreviewInvalidated += InvalidatePreviewImage;
        }
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (Vm is not null)
                Vm.PreviewInvalidated -= InvalidatePreviewImage;
        }
        private void InvalidatePreviewImage()
        {
            PreviewImage?.InvalidateVisual();
        }
        private void DrawCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var p = e.GetCurrentPoint(DrawCanvas);
            // 좌클릭만 허용
            if (!p.Properties.IsLeftButtonPressed) return;

            _isDragging = true;
            _start = e.GetPosition(DrawCanvas);

            DrawingRect.IsVisible = true;
            Canvas.SetLeft(DrawingRect, _start.X);
            Canvas.SetTop(DrawingRect, _start.Y);
            DrawingRect.Width = 0;
            DrawingRect.Height = 0;

            DrawCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void DrawCanvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;

            var cur = e.GetPosition(DrawCanvas);

            var x = Math.Min(_start.X, cur.X);
            var y = Math.Min(_start.Y, cur.Y);
            var w = Math.Abs(cur.X - _start.X);
            var h = Math.Abs(cur.Y - _start.Y);

            Canvas.SetLeft(DrawingRect, x);
            Canvas.SetTop(DrawingRect, y);
            DrawingRect.Width = w;
            DrawingRect.Height = h;

            e.Handled = true;
        }

        private void DrawCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            DrawCanvas.ReleasePointerCapture(e.Pointer);

            if (Vm is not null)
            {
                // ? 548x548 고정 좌표계이므로 변환 없이 그대로 VM에 전달
                var rect = new Rect(
                    Canvas.GetLeft(DrawingRect),
                    Canvas.GetTop(DrawingRect),
                    DrawingRect.Width,
                    DrawingRect.Height);

                // 너무 작은 노이즈 클릭 방지 (1x1 이상만)
                if (rect.Width >= 1 && rect.Height >= 1)
                {
                    Vm.AddRoiCommand.Execute(rect);
                }
            }

            // 드래그용 사각형 초기화
            DrawingRect.IsVisible = false;
            DrawingRect.Width = 0;
            DrawingRect.Height = 0;

            e.Handled = true;
        }
    }
}