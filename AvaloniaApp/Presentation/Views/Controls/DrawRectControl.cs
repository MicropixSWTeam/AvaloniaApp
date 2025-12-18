using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaApp.Core.Models;
using System.Collections;
using System.Collections.Specialized;

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

        // 15색 팔레트
        private static readonly IBrush[] _palette = new IBrush[]
        {
            Brushes.Lime, Brushes.Cyan, Brushes.Yellow, Brushes.Orange, Brushes.Magenta,
            Brushes.Red, Brushes.DodgerBlue, Brushes.SpringGreen, Brushes.Gold, Brushes.DeepPink,
            Brushes.Coral, Brushes.Aqua, Brushes.Chartreuse, Brushes.Violet, Brushes.White
        };

        static DrawRectControl()
        {
            RegionsProperty.Changed.AddClassHandler<DrawRectControl>((x, e) => x.OnRegionsChanged(e));
        }

        private void OnRegionsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (_incc is not null)
                _incc.CollectionChanged -= OnCollectionChanged;

            _incc = e.NewValue as INotifyCollectionChanged;
            if (_incc is not null)
                _incc.CollectionChanged += OnCollectionChanged;

            InvalidateVisual();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Regions is null) return;

            foreach (var obj in Regions)
            {
                if (obj is not SelectRegionData r) continue;

                var rect = r.ControlRect;
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var brush = _palette[r.ColorIndex % _palette.Length];
                var pen = new Pen(brush, 1);

                // ✅ 테두리만 그리기
                context.DrawRectangle(null, pen, rect);
            }
        }
    }
}