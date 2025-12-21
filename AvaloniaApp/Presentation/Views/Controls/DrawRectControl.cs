using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaApp.Configuration; // 가정
using AvaloniaApp.Core.Models;   // 가정
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;

namespace AvaloniaApp.Presentation.Views.Controls
{
    public sealed class DrawRectControl : Control
    {
        // 1. Regions 속성 등록
        // AffectsRender를 통해 이 속성이 바뀌면 자동으로 Render가 호출됨을 명시합니다.
        // (단, 컬렉션 내부 변경은 별도로 처리해야 함)
        public static readonly StyledProperty<IEnumerable?> RegionsProperty =
            AvaloniaProperty.Register<DrawRectControl, IEnumerable?>(nameof(Regions));

        public IEnumerable? Regions
        {
            get => GetValue(RegionsProperty);
            set => SetValue(RegionsProperty, value);
        }

        static DrawRectControl()
        {
            // 이 속성이 바뀌면 화면을 다시 그려야 한다고 Avalonia 렌더링 시스템에 알림
            AffectsRender<DrawRectControl>(RegionsProperty);
        }

        private INotifyCollectionChanged? _incc;

        // 2. 속성 변경 감지 (AddClassHandler 대신 OnPropertyChanged 오버라이드 권장)
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == RegionsProperty)
            {
                var oldIncc = change.OldValue as INotifyCollectionChanged;
                var newIncc = change.NewValue as INotifyCollectionChanged;

                if (oldIncc != null)
                {
                    oldIncc.CollectionChanged -= OnCollectionChanged;
                }

                if (newIncc != null)
                {
                    newIncc.CollectionChanged += OnCollectionChanged;
                }

                // AffectsRender가 처리해주지만, 안전하게 한 번 더 호출해도 무방
                InvalidateVisual();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 컬렉션 내부 아이템이 추가/삭제되면 다시 그림
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            // [성능 팁] Regions에 자주 접근하므로 로컬 변수에 할당
            var regions = Regions;
            if (regions is null) return;

            foreach (var obj in regions)
            {
                if (obj is not RegionData r) continue;

                var rect = r.Rect;
                // 너비나 높이가 0 이하면 그리지 않음 (방어 코드 Good)
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var brush = Options.GetBrushByIndex(r.Index);
                // [메모리 팁] Pen을 매번 생성하는 것은 비용이 듭니다. 
                // 가능하다면 캐싱하거나 ImmutablePen을 사용하는 것이 좋지만, 
                // 갯수가 적다면 현재 방식도 OK.
                var pen = new Pen(brush, 1);

                context.DrawRectangle(null, pen, rect);

                // FormattedText는 무거운 객체입니다. 
                // 프레임 드랍이 발생한다면 캐싱을 고려해야 합니다.
                var ft = new FormattedText(
                    (r.Index + 1).ToString(),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default, // 폰트 로드 비용 절감
                    14,
                    brush);

                context.DrawText(ft, new Point(rect.X, rect.Y - 18));
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // 메모리 누수 방지: 뷰에서 떨어져 나갈 때 이벤트 구독 해지 (필수)
            if (_incc is not null)
            {
                _incc.CollectionChanged -= OnCollectionChanged;
                _incc = null;
            }
        }
    }
}