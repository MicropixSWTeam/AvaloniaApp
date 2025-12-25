using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using AvaloniaApp.Configuration; // Options.GetBrushByIndex 등
using AvaloniaApp.Core.Models;   // RegionData
using System;
using System.Collections;
using System.Linq;
using System.Windows.Input;
using AvaloniaEdit.Utils;

namespace AvaloniaApp.Presentation.Views.UserControls;

public partial class DrawCanvasView : UserControl
{
    // 1. Regions (바인딩용)
    public static readonly StyledProperty<IEnumerable?> RegionsProperty =
        AvaloniaProperty.Register<DrawCanvasView, IEnumerable?>(nameof(Regions));

    public IEnumerable? Regions
    {
        get => GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    // 2. Commands (추가/삭제)
    public static readonly StyledProperty<ICommand?> AddCommandProperty =
        AvaloniaProperty.Register<DrawCanvasView, ICommand?>(nameof(AddCommand));

    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<DrawCanvasView, ICommand?>(nameof(RemoveCommand));

    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    // 3. Color Index (현재 그리기 색상)
    public static readonly StyledProperty<int> ColorIndexProperty =
        AvaloniaProperty.Register<DrawCanvasView, int>(nameof(ColorIndex), defaultValue: 0);

    public int ColorIndex
    {
        get => GetValue(ColorIndexProperty);
        set => SetValue(ColorIndexProperty, value);
    }

    // 내부 상태 변수
    private bool _isDragging;
    private Point _startPoint;
    private const double HitTolerance = 10.0;

    public DrawCanvasView()
    {
        InitializeComponent();
        InteractionCanvas.PointerCaptureLost += OnPointerCaptureLost;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        InteractionCanvas.PointerCaptureLost -= OnPointerCaptureLost;
    }

    // --- 로직 이식 ---
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(InteractionCanvas);

        // [우클릭: 삭제 로직]
        if (point.Properties.IsRightButtonPressed)
        {
            HandleRightClick(point.Position);
            e.Handled = true;
            return;
        }

        // [좌클릭: 그리기 시작]
        if (point.Properties.IsLeftButtonPressed && ColorIndex != -1)
        {
            DragRect.Stroke = Options.GetBrushByIndex(ColorIndex);
            StartDragging(point.Position);
            InteractionCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void HandleRightClick(Point pos)
    {
        if (Regions == null || RemoveCommand == null) return;

        // 화면상 위에 있는(나중에 그려진) 요소부터 검사
        // Regions는 IEnumerable이므로 Cast 필요
        var regionList = Regions.OfType<RegionData>().Reverse();

        var target = regionList.FirstOrDefault(r =>
        {
            // 점 자체 혹은 라벨 영역 히트 테스트 
            bool hitSpot = r.Rect.Inflate(HitTolerance).Contains(pos);
            var labelRect = new Rect(r.Rect.X, r.Rect.Y - 25, 30, 25);
            bool hitLabel = labelRect.Contains(pos);
            return hitSpot || hitLabel;
        });

        if (target != null && RemoveCommand.CanExecute(target))
        {
            RemoveCommand.Execute(target);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var currentPoint = e.GetPosition(InteractionCanvas);
        UpdateDrawingRect(_startPoint, currentPoint);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        InteractionCanvas.ReleasePointerCapture(e.Pointer);

        var endPoint = e.GetPosition(InteractionCanvas);
        var bounds = new Rect(InteractionCanvas.Bounds.Size);
        var constrainedEnd = Clamp(endPoint, bounds);
        var finalRect = GetNormalizedRect(_startPoint, constrainedEnd);

        // 최소 크기 보정
        var width = Math.Max(1.0, finalRect.Width);
        var height = Math.Max(1.0, finalRect.Height);
        var adjustedRect = new Rect(finalRect.Position, new Size(width, height));

        if (AddCommand != null && AddCommand.CanExecute(adjustedRect))
        {
            AddCommand.Execute(adjustedRect);
        }

        ResetDrawingOverlay();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ResetDrawingOverlay();
        }
    }

    // Helpers (기존 코드 재사용)
    private void StartDragging(Point start)
    {
        _isDragging = true;
        _startPoint = start;
        DragRect.IsVisible = true;
        UpdateDrawingRect(start, start);
    }

    private void UpdateDrawingRect(Point p1, Point p2)
    {
        var bounds = new Rect(InteractionCanvas.Bounds.Size);
        var constrainedP2 = Clamp(p2, bounds);
        var rect = GetNormalizedRect(p1, constrainedP2);

        Canvas.SetLeft(DragRect, rect.X);
        Canvas.SetTop(DragRect, rect.Y);
        DragRect.Width = rect.Width;
        DragRect.Height = rect.Height;
    }

    private void ResetDrawingOverlay()
    {
        DragRect.IsVisible = false;
        DragRect.Width = 0;
        DragRect.Height = 0;
    }

    private static Rect GetNormalizedRect(Point p1, Point p2) =>
        new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));

    private static Point Clamp(Point p, Rect r) =>
        new(Math.Clamp(p.X, 0, r.Width), Math.Clamp(p.Y, 0, r.Height));
}