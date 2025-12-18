using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;

namespace AvaloniaApp.Presentation.Views.Controls
{
    public sealed class DrawRectControl : Control
    {
        public static readonly StyledProperty<IEnumerable?> RegionsProperty =
            AvaloniaProperty.Register<DrawRectControl, IEnumerable?>(nameof(Regions));

        public IEnumerable? Regions
        {
            get => GetValue(RegionsProperty);
            set => SetValue(RegionsProperty, value);
        }

        private INotifyCollectionChanged? _incc;

        static DrawRectControl()
        {
            RegionsProperty.Changed.AddClassHandler<DrawRectControl>((x, e) => x.OnRegionsChanged(e));
        }

        private void OnRegionsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_incc is not null) _incc.CollectionChanged -= OnCollectionChanged;
            _incc = e.NewValue as INotifyCollectionChanged;
            if (_incc is not null) _incc.CollectionChanged += OnCollectionChanged;
            InvalidateVisual();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Regions is null) return;

            foreach (var obj in Regions)
            {
                if (obj is not SelectRegionData r) continue;

                var rect = r.ControlRect;
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                // 공통 팔레트 사용
                var brush = Options.GetBrushByIndex(r.ColorIndex);
                var pen = new Pen(brush, 1.5);

                context.DrawRectangle(null, pen, rect);

                // [UX] 사각형 번호 표시 (차트 연동 시 식별용)
                var ft = new FormattedText(
                    (r.ColorIndex + 1).ToString(),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    14,
                    brush);

                context.DrawText(ft, new Point(rect.X, rect.Y - 18));
            }
        }
    }
}