// AvaloniaApp.Presentation/Views/UserControls/CameraView.xaml.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaEdit.Utils;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class CameraView : UserControl
    {
        private bool _isDragging;
        private Point _dragStart;
        private Action? _invalidateHandler;
        public CameraView()
        {
            InitializeComponent();
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
            //SelectionCanvas.SizeChanged += SelectionCanvas_OnSizeChanged;
        }

        private CameraViewModelTest? Vm => DataContext as CameraViewModelTest;

        private void SelectionCanvas_OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (Vm is null) return;
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
        //// 실제 화면에 렌더된 이미지 영역(Rect)을 계산 (Image.Stretch = Uniform 기준)
        //private bool TryGetImageRenderRect(out Rect rect)
        //{
        //    rect = new Rect();

        //    if (Vm?.DisplayImage is not Bitmap bmp)
        //        return false;

        //    var controlSize = SelectionCanvas.Bounds.Size;
        //    var pixelSize = bmp.PixelSize;

        //    if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
        //        pixelSize.Width <= 0 || pixelSize.Height <= 0)
        //        return false;

        //    double scale = Math.Min(
        //        controlSize.Width / pixelSize.Width,
        //        controlSize.Height / pixelSize.Height);

        //    double imgWidth = pixelSize.Width * scale;
        //    double imgHeight = pixelSize.Height * scale;

        //    double offsetX = (controlSize.Width - imgWidth) * 0.5;
        //    double offsetY = (controlSize.Height - imgHeight) * 0.5;

        //    rect = new Rect(offsetX, offsetY, imgWidth, imgHeight);
        //    return true;
        //}

        //private static Point ClampToRect(Point p, Rect rect)
        //{
        //    var x = Math.Clamp(p.X, rect.X, rect.X + rect.Width);
        //    var y = Math.Clamp(p.Y, rect.Y, rect.Y + rect.Height);
        //    return new Point(x, y);
        //}

        //private void SelectionCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        //{
        //    if (Vm is null) return;
        //    if (!Vm.CanDrawRegions) return;

        //    if (!TryGetImageRenderRect(out var imageRect))
        //        return;

        //    var pos = e.GetPosition(SelectionCanvas);

        //    // 이미지 영역 밖에서 시작하면 무시
        //    if (!imageRect.Contains(pos))
        //        return;

        //    _isDragging = true;
        //    _dragStart = ClampToRect(pos, imageRect);
        //    SelectionCanvas.CapturePointer(e.Pointer);

        //    Canvas.SetLeft(SelectionRect, _dragStart.X);
        //    Canvas.SetTop(SelectionRect, _dragStart.Y);
        //    SelectionRect.Width = 0;
        //    SelectionRect.Height = 0;

        //    Vm.SelectionRectInControl = new Rect(_dragStart, _dragStart);

        //    e.Handled = true;
        //}

        //private void SelectionCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
        //{
        //    if (!_isDragging || Vm is null) return;

        //    if (!TryGetImageRenderRect(out var imageRect))
        //        return;

        //    var raw = e.GetPosition(SelectionCanvas);
        //    var current = ClampToRect(raw, imageRect);

        //    double x = Math.Min(_dragStart.X, current.X);
        //    double y = Math.Min(_dragStart.Y, current.Y);
        //    double w = Math.Abs(current.X - _dragStart.X);
        //    double h = Math.Abs(current.Y - _dragStart.Y);

        //    // ★ 최소 1픽셀 보장
        //    var rect = EnforceMinSize(new Rect(new Point(x, y), new Size(w, h)), imageRect);

        //    Canvas.SetLeft(SelectionRect, rect.X);
        //    Canvas.SetTop(SelectionRect, rect.Y);
        //    SelectionRect.Width = rect.Width;
        //    SelectionRect.Height = rect.Height;

        //    Vm.SelectionRectInControl = rect;

        //    e.Handled = true;
        //}
        //private void SelectionCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        //{
        //    if (!_isDragging || Vm is null) return;

        //    _isDragging = false;
        //    SelectionCanvas.ReleasePointerCapture(e.Pointer);

        //    if (!TryGetImageRenderRect(out var imageRect))
        //        return;

        //    var rawEnd = e.GetPosition(SelectionCanvas);
        //    var end = ClampToRect(rawEnd, imageRect);

        //    double x = Math.Min(_dragStart.X, end.X);
        //    double y = Math.Min(_dragStart.Y, end.Y);
        //    double w = Math.Abs(end.X - _dragStart.X);
        //    double h = Math.Abs(end.Y - _dragStart.Y);

        //    // ★ 최소 1픽셀 보장
        //    var rect = EnforceMinSize(new Rect(new Point(x, y), new Size(w, h)), imageRect);

        //    Vm.SelectionRectInControl = rect;

        //    // 여기서 Region 추가 + Chart 업데이트
        //    Vm.CommitSelectionRect();

        //    SelectionRect.Width = 0;
        //    SelectionRect.Height = 0;

        //    e.Handled = true;
        //}

        //// 최소 선택 크기를 보장하기 위한 유틸리티
        //private static Rect EnforceMinSize(Rect rect, Rect imageRect, double minSize = 1.0)
        //{
        //    var x = rect.X;
        //    var y = rect.Y;
        //    var w = rect.Width;
        //    var h = rect.Height;

        //    // 폭 최소 보장
        //    if (w < minSize)
        //    {
        //        var centerX = x + w / 2.0;
        //        x = Math.Clamp(centerX - minSize / 2.0, imageRect.X, imageRect.Right - minSize);
        //        w = minSize;
        //    }

        //    // 높이 최소 보장
        //    if (h < minSize)
        //    {
        //        var centerY = y + h / 2.0;
        //        y = Math.Clamp(centerY - minSize / 2.0, imageRect.Y, imageRect.Bottom - minSize);
        //        h = minSize;
        //    }

        //    return new Rect(new Point(x, y), new Size(w, h));
        //}
    }
}
