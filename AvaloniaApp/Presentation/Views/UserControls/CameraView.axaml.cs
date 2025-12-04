using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Presentation.ViewModels.UserControls;
using System;

namespace AvaloniaApp.Presentation.Views.UserControls
{
    public partial class CameraView : UserControl
    {
        private bool _isDragging;
        private Point _dragStart;

        // DataContext를 강하게 캐스팅해서 쓰기 위한 헬퍼
        private CameraViewModel? Vm => DataContext as CameraViewModel;

        public CameraView()
        {
            InitializeComponent();

            // 선택 중일 때 마우스 커서 모양(선택 사항)
            SelectionCanvas.Cursor = new Cursor(StandardCursorType.Cross);
        }

        private void SelectionCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm is null) return;

            // 스트리밍 중일 때는 선택 비활성화 (Stop 누른 후에만 선택)
            if (!Vm.IsStop) return;

            var point = e.GetPosition(SelectionCanvas);

            _isDragging = true;
            _dragStart = point;

            // 초기 rect 0으로 시작
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            Canvas.SetLeft(SelectionRect, point.X);
            Canvas.SetTop(SelectionRect, point.Y);

            // 공식 API: IPointer.Capture(IInputElement)
            // https://api-docs.avaloniaui.net/docs/M_Avalonia_Input_IPointer_Capture
            e.Pointer.Capture(SelectionCanvas);
        }

        private void SelectionCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            if (Vm is null) return;
            if (!Vm.IsStop) return;

            var p = e.GetPosition(SelectionCanvas);

            var x = Math.Min(_dragStart.X, p.X);
            var y = Math.Min(_dragStart.Y, p.Y);
            var w = Math.Abs(p.X - _dragStart.X);
            var h = Math.Abs(p.Y - _dragStart.Y);

            SelectionRect.Width = w;
            SelectionRect.Height = h;
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);

            // VM에 현재 컨트롤 크기 + 선택 영역 전달
            Vm.ImageControlSize = SelectionCanvas.Bounds.Size;
            Vm.SelectionRectInControl = new Rect(x, y, w, h);
        }

        private void SelectionCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;

            // 캡처 해제
            if (e.Pointer.Captured == SelectionCanvas)
                e.Pointer.Capture(null);

            if (Vm is null) return;
            if (!Vm.IsStop) return;

            Vm.ImageControlSize = SelectionCanvas.Bounds.Size;

            // 여기서 선택된 영역의 Y 평균/표준편차 계산 호출
            Vm.UpdateSelectionStats();
        }
    }
}
