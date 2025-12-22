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

    // 점 자체를 클릭할 때의 여유 범위 (10px)
    private const double HitTolerance = 10.0;

    private RawImageViewModel? ViewModel => DataContext as RawImageViewModel;
    public RawImageView()
    {
        InitializeComponent();
        DrawCanvas.PointerCaptureLost += DrawCanvas_PointerCaptureLost;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.PreviewInvalidated += InvalidatePreviewImage;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel != null)
            ViewModel.CameraVM.PreviewInvalidated -= InvalidatePreviewImage;

        DrawCanvas.PointerCaptureLost -= DrawCanvas_PointerCaptureLost;
    }
    private void InvalidatePreviewImage() => PreviewImage?.InvalidateVisual();

    #region Interaction Handling
    private void DrawCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null) return;
        var point = e.GetCurrentPoint(DrawCanvas);

        // [우클릭 삭제 로직: 점 자체 OR 숫자 라벨 클릭 감지]
        if (point.Properties.IsRightButtonPressed)
        {
            var pos = point.Position;
            var regions = ViewModel.CameraVM.Regions;

            if (regions != null)
            {
                // 화면상 위에 있는(나중에 그려진) 요소부터 검사
                var target = regions.Reverse().FirstOrDefault(r =>
                {
                    // 1. 점(Rect) 자체 클릭 (주변 10px 여유 허용)
                    bool hitSpot = r.Rect.Inflate(HitTolerance).Contains(pos);

                    // 2. 숫자 라벨(Text) 클릭 감지
                    //    DrawRectControl 기준 (X, Y - 18) 위치 고려
                    //    (위치: 점 위쪽 Y-25 ~ Y, 너비: 숫자 고려 30px)
                    var labelRect = new Rect(r.Rect.X, r.Rect.Y - 25, 30, 25);
                    bool hitLabel = labelRect.Contains(pos);

                    return hitSpot || hitLabel;
                });

                if (target != null)
                {
                    ViewModel.CameraVM.RemoveRegionCommand.Execute(target);
                    e.Handled = true;
                }
            }
            return;
        }

        // --- 좌클릭(그리기) 로직 ---

        if (ViewModel.CameraVM.NextAvailableRegionColorIndex == -1) return;
        if (!point.Properties.IsLeftButtonPressed) return;

        var position = point.Position;
        var canvasRect = new Rect(DrawCanvas.Bounds.Size);
        if (!canvasRect.Contains(position)) return;

        int nextIndex = ViewModel.CameraVM.NextAvailableRegionColorIndex;
        DrawingRect.Stroke = Options.GetBrushByIndex(nextIndex);

        StartDragging(position);
        DrawCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DrawCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var currentPoint = e.GetPosition(DrawCanvas);
        UpdateDrawingRect(_startPoint, currentPoint);
        e.Handled = true;
    }
    private void DrawCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        DrawCanvas.ReleasePointerCapture(e.Pointer);

        var endPoint = e.GetPosition(DrawCanvas);
        var canvasBounds = new Rect(DrawCanvas.Bounds.Size);
        var constrainedEnd = Clamp(endPoint, canvasBounds);

        var finalRect = GetNormalizedRect(_startPoint, constrainedEnd);

        var width = Math.Max(1.0, finalRect.Width);
        var height = Math.Max(1.0, finalRect.Height);
        var adjustedRect = new Rect(finalRect.Position, new Size(width, height));

        ViewModel?.CameraVM.AddRegionCommand.Execute(adjustedRect);

        ResetDrawingOverlay();
        e.Handled = true;
    }
    private void DrawCanvas_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ResetDrawingOverlay();
        }
    }
    #endregion

    #region Helpers
    private void StartDragging(Point start)
    {
        _isDragging = true;
        _startPoint = start;
        DrawingRect.IsVisible = true;
        UpdateDrawingRect(start, start);
    }
    private void UpdateDrawingRect(Point p1, Point p2)
    {
        var bounds = new Rect(DrawCanvas.Bounds.Size);
        var constrainedP2 = Clamp(p2, bounds);
        var rect = GetNormalizedRect(p1, constrainedP2);

        Canvas.SetLeft(DrawingRect, rect.X);
        Canvas.SetTop(DrawingRect, rect.Y);
        DrawingRect.Width = rect.Width;
        DrawingRect.Height = rect.Height;
    }
    private void ResetDrawingOverlay()
    {
        DrawingRect.IsVisible = false;
        DrawingRect.Width = 0;
        DrawingRect.Height = 0;
    }
    private static Rect GetNormalizedRect(Point p1, Point p2) => new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));
    private static Point Clamp(Point p, Rect r) => new(Math.Clamp(p.X, 0, r.Width), Math.Clamp(p.Y, 0, r.Height));

    #endregion
}