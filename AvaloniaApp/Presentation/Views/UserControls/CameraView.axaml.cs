using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using AvaloniaEdit.Utils;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls;

public partial class CameraView : UserControl
{
    private bool _isDragging;
    private Point _startPoint;

    private CameraViewModel? ViewModel => DataContext as CameraViewModel;

    public CameraView()
    {
        InitializeComponent();
        DrawCanvas.PointerCaptureLost += DrawCanvas_PointerCaptureLost;
    }

    // LifeCycle 이벤트를 Override하여 가독성 및 자원 관리 향상
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel != null)
            ViewModel.PreviewInvalidated += InvalidatePreviewImage;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ViewModel != null)
            ViewModel.PreviewInvalidated -= InvalidatePreviewImage;

        // 이벤트 해제 (메모리 누수 방지)
        DrawCanvas.PointerCaptureLost -= DrawCanvas_PointerCaptureLost;
    }

    private void InvalidatePreviewImage() => PreviewImage?.InvalidateVisual();

    #region Interaction Handling

    private void DrawCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null) return;

        // 1. 개수 제한 체크: 이미 6개면 드래그 시작도 안 함
        if (ViewModel.NextAvailableRegionColorIndex == -1)
        {
            // 시각적 피드백이 필요하다면 여기서 알림 처리 가능
            return;
        }

        var point = e.GetCurrentPoint(DrawCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var position = point.Position;
        var canvasRect = new Rect(DrawCanvas.Bounds.Size);
        if (!canvasRect.Contains(position)) return;

        // 2. 색상 결정: 다음에 그려질 실제 색상을 DrawingRect에 적용
        int nextIndex = ViewModel.NextAvailableRegionColorIndex;
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

        ViewModel?.AddRegionCommand.Execute(adjustedRect);

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

    private static Rect GetNormalizedRect(Point p1, Point p2)
        => new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));

    private static Point Clamp(Point p, Rect r)
        => new(Math.Clamp(p.X, 0, r.Width), Math.Clamp(p.Y, 0, r.Height));

    #endregion
}