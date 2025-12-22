using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace AvaloniaApp.Presentation.Views.Controls;

public readonly record struct ChartPoint(int Wavelength, byte Mean, byte? StdDev = null);

public sealed class ChartSeries
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; init; } = "Name";

    private IReadOnlyList<ChartPoint> _points = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> Points
    {
        get => _points;
        set { _points = value ?? Array.Empty<ChartPoint>(); Version++; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public int Version { get; private set; }
    public event EventHandler? Changed;

    public IPen LinePen { get; init; } = new Pen(Brushes.White, 1);
    public IBrush MarkerFill { get; init; } = Brushes.White;
    public double MarkerRadius { get; init; } = 3;
    public bool ShowErrorBars { get; init; } = false;
    public double ErrorCapHalfWidth { get; init; } = 3;
}

public sealed class ChartControl : Control
{
    public static readonly StyledProperty<ObservableCollection<ChartSeries>> SeriesProperty =
        AvaloniaProperty.Register<ChartControl, ObservableCollection<ChartSeries>>(nameof(Series));
    public ObservableCollection<ChartSeries> Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    public static readonly StyledProperty<int> XMinProperty = AvaloniaProperty.Register<ChartControl, int>(nameof(XMin), 400);
    public static readonly StyledProperty<int> XMaxProperty = AvaloniaProperty.Register<ChartControl, int>(nameof(XMax), 700);

    public static readonly StyledProperty<byte> YMinProperty = AvaloniaProperty.Register<ChartControl, byte>(nameof(YMin), 0);
    public static readonly StyledProperty<byte> YMaxProperty = AvaloniaProperty.Register<ChartControl, byte>(nameof(YMax), 255);

    public int XMin { get => GetValue(XMinProperty); set => SetValue(XMinProperty, value); }
    public int XMax { get => GetValue(XMaxProperty); set => SetValue(XMaxProperty, value); }
    public byte YMin { get => GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public byte YMax { get => GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }

    public static readonly StyledProperty<IReadOnlyList<int>> XTicksProperty =
        AvaloniaProperty.Register<ChartControl, IReadOnlyList<int>>(nameof(XTicks),
            new int[] { 410, 430, 450, 470, 490, 510, 530, 550, 570, 590, 610, 630, 650, 670, 690 });
    public IReadOnlyList<int> XTicks { get => GetValue(XTicksProperty); set => SetValue(XTicksProperty, value); }

    public static readonly StyledProperty<IReadOnlyList<byte>> YTicksProperty =
        AvaloniaProperty.Register<ChartControl, IReadOnlyList<byte>>(nameof(YTicks),
            new byte[] { 0, 50, 100, 150, 200, 250 });
    public IReadOnlyList<byte> YTicks { get => GetValue(YTicksProperty); set => SetValue(YTicksProperty, value); }

    public static readonly StyledProperty<bool> AutoScaleProperty = AvaloniaProperty.Register<ChartControl, bool>(nameof(AutoScale), true);
    public bool AutoScale { get => GetValue(AutoScaleProperty); set => SetValue(AutoScaleProperty, value); }

    public static readonly StyledProperty<double> RangePaddingProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(RangePadding), 0.1);
    public double RangePadding { get => GetValue(RangePaddingProperty); set => SetValue(RangePaddingProperty, value); }

    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChartControl, IBrush>(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#0C111A")));
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }

    public static readonly Thickness PlotPadding = new Thickness(50, 20, 20, 40);

    private RenderTargetBitmap? _graphLayerBitmap;
    private bool _isGraphDirty = true;
    private bool _pendingRender = false;

    private PixelSize _lastPixelSize;
    private double _lastScaling;

    private static readonly PointXComparer _xComparer = new();

    // [Stateless] 마우스 위치만 저장
    private Point? _lastMousePosition = null;

    private sealed class SeriesCache
    {
        public StreamGeometry? LineGeom;
        public StreamGeometry? MarkerGeom;
        public StreamGeometry? ErrorBarGeom;
        public Point[]? PooledPixelPoints;
        public int PixelCount;
        public int LastVersion = -1;
        public Rect LastPlot;
        public (int xmin, int xmax, byte ymin, byte ymax) LastRange;

        public void ReturnPool()
        {
            if (PooledPixelPoints is not null) { ArrayPool<Point>.Shared.Return(PooledPixelPoints, false); PooledPixelPoints = null; }
            PixelCount = 0; LineGeom = null; MarkerGeom = null; ErrorBarGeom = null;
        }
    }

    private sealed class PointXComparer : IComparer<Point>
    {
        public int Compare(Point x, Point y) => x.X.CompareTo(y.X);
    }

    private readonly Dictionary<string, SeriesCache> _seriesCache = new();

    public ChartControl()
    {
        Series = new ObservableCollection<ChartSeries>();
        AttachCollection(Series);
        ClipToBounds = true;

        PointerMoved += OnPointerMovedInternal;
        // 마우스 나가면 툴팁 제거
        PointerExited += (_, __) => { _lastMousePosition = null; InvalidateVisual(); };
    }

    private void AttachCollection(ObservableCollection<ChartSeries>? col)
    {
        if (col == null) return;
        col.CollectionChanged += OnSeriesCollectionChanged;
        foreach (var s in col) s.Changed += OnSeriesDataChanged;
    }

    private void DetachCollection(ObservableCollection<ChartSeries>? col)
    {
        if (col == null) return;
        col.CollectionChanged -= OnSeriesCollectionChanged;
        foreach (var s in col) s.Changed -= OnSeriesDataChanged;
    }

    private void OnSeriesDataChanged(object? sender, EventArgs e)
    {
        if (AutoScale) UpdateAutoScale();
        _isGraphDirty = true;

        if (!_pendingRender)
        {
            _pendingRender = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_pendingRender)
                {
                    InvalidateVisual();
                    _pendingRender = false;
                }
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void OnSeriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (ChartSeries s in e.NewItems) s.Changed += OnSeriesDataChanged;
        if (e.OldItems != null) foreach (ChartSeries s in e.OldItems) { s.Changed -= OnSeriesDataChanged; if (_seriesCache.TryGetValue(s.Id, out var c)) c.ReturnPool(); _seriesCache.Remove(s.Id); }

        if (AutoScale) UpdateAutoScale();
        _isGraphDirty = true;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeriesProperty)
        {
            DetachCollection(change.OldValue as ObservableCollection<ChartSeries>);
            AttachCollection(change.NewValue as ObservableCollection<ChartSeries>);
            _isGraphDirty = true;
        }
        else if (change.Property == XMinProperty || change.Property == XMaxProperty ||
                 change.Property == YMinProperty || change.Property == YMaxProperty ||
                 change.Property == XTicksProperty || change.Property == YTicksProperty ||
                 change.Property == BackgroundBrushProperty)
        {
            _isGraphDirty = true;
        }
        InvalidateVisual();
    }

    private void UpdateAutoScale()
    {
        if (Series == null || Series.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue;
        bool hasData = false;

        foreach (var s in Series)
        {
            if (s.Points.Count == 0) continue;
            hasData = true;
            foreach (var p in s.Points)
            {
                minX = Math.Min(minX, p.Wavelength);
                maxX = Math.Max(maxX, p.Wavelength);
            }
        }
        if (!hasData) return;

        double pad = RangePadding;
        int dx = maxX - minX; if (dx == 0) dx = 1;

        SetCurrentValue(XMinProperty, (int)(minX - dx * pad));
        SetCurrentValue(XMaxProperty, (int)(maxX + dx * pad));

        SetCurrentValue(YMinProperty, (byte)0);
        SetCurrentValue(YMaxProperty, (byte)255);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var plot = bounds.Deflate(PlotPadding);
        var range = (xmin: XMin, xmax: XMax, ymin: YMin, ymax: YMax);

        UpdateGraphLayerIfDirty(bounds, plot, range);

        if (_graphLayerBitmap != null)
            context.DrawImage(_graphLayerBitmap, new Rect(0, 0, _graphLayerBitmap.PixelSize.Width, _graphLayerBitmap.PixelSize.Height), bounds);

        // 매 렌더링마다 툴팁 새로 계산 (Stateless)
        DrawOverlay(context, bounds, plot);
    }

    private void DrawOverlay(DrawingContext context, Rect bounds, Rect plot)
    {
        if (_lastMousePosition == null) return;
        Point pos = _lastMousePosition.Value;
        if (!plot.Contains(pos)) return;

        double bestD2 = 625; // 25px
        string? bestText = null;
        Point bestPos = default;

        foreach (var s in Series)
        {
            if (!_seriesCache.TryGetValue(s.Id, out var cache) || cache.PooledPixelPoints == null) continue;

            int idx = Array.BinarySearch(cache.PooledPixelPoints, 0, cache.PixelCount, new Point(pos.X, 0), _xComparer);
            if (idx < 0) idx = ~idx;

            int start = Math.Max(0, idx - 3);
            int end = Math.Min(cache.PixelCount, idx + 3);

            for (int i = start; i < end; i++)
            {
                var pix = cache.PooledPixelPoints[i];
                double d2 = (pix.X - pos.X) * (pix.X - pos.X) + (pix.Y - pos.Y) * (pix.Y - pos.Y);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestPos = pix;
                    bestText = $"{s.DisplayName}\nMean : {s.Points[i].Mean}\nStdDev : {s.Points[i].StdDev}";
                }
            }
        }

        if (bestText != null)
        {
            var formatted = new FormattedText(bestText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);

            var rect = new Rect(bestPos.X + 12, bestPos.Y - formatted.Height - 12, formatted.Width + 10, formatted.Height + 6);
            if (rect.Right > bounds.Right) rect = rect.WithX(bestPos.X - rect.Width - 12);
            if (rect.Top < bounds.Top) rect = rect.WithY(bestPos.Y + 12);

            context.FillRectangle(new SolidColorBrush(Color.Parse("#E6222222")), rect, 4);
            context.DrawText(formatted, new Point(rect.X + 5, rect.Y + 3));
            context.DrawEllipse(null, new Pen(Brushes.White, 2), bestPos, 5, 5);
        }
    }

    private void UpdateGraphLayerIfDirty(Rect bounds, Rect plot, (int xmin, int xmax, byte ymin, byte ymax) range)
    {
        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        var pixelSize = PixelSize.FromSize(bounds.Size, scaling);

        if (_graphLayerBitmap == null || _lastPixelSize != pixelSize || _lastScaling != scaling || _isGraphDirty)
        {
            _graphLayerBitmap?.Dispose();
            _graphLayerBitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
            _lastPixelSize = pixelSize;
            _lastScaling = scaling;
            _isGraphDirty = false;

            using var dc = _graphLayerBitmap.CreateDrawingContext();

            dc.FillRectangle(BackgroundBrush, bounds);
            dc.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#33FFFFFF")), 1), plot);

            if (range.xmax <= range.xmin || range.ymax <= range.ymin) return;

            double sx = plot.Width / (range.xmax - range.xmin);
            double sy = plot.Height / (range.ymax - range.ymin);
            double ox = plot.Left - (double)range.xmin * sx;
            double oy = plot.Bottom + (double)range.ymin * sy;

            DrawGridAndLabels(dc, plot, sx, sy, ox, oy);

            using (dc.PushClip(plot))
            {
                EnsureSeriesCache(plot, range, sx, sy, ox, oy);
                foreach (var s in Series)
                {
                    if (!_seriesCache.TryGetValue(s.Id, out var cache)) continue;
                    if (cache.LineGeom != null) dc.DrawGeometry(null, s.LinePen, cache.LineGeom);
                    if (s.ShowErrorBars && cache.ErrorBarGeom != null) dc.DrawGeometry(null, s.LinePen, cache.ErrorBarGeom);
                    if (cache.MarkerGeom != null) dc.DrawGeometry(s.MarkerFill, null, cache.MarkerGeom);
                }
            }
        }
    }

    private void DrawGridAndLabels(DrawingContext dc, Rect plot, double sx, double sy, double ox, double oy)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#12FFFFFF")), 1);
        var lb = new SolidColorBrush(Color.Parse("#99FFFFFF"));
        var tf = new Typeface(FontFamily.Default);

        if (YTicks != null) foreach (var t in YTicks)
            {
                double y = oy - (double)t * sy;
                if (y < plot.Top - 10 || y > plot.Bottom + 10) continue;

                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var ft = new FormattedText(t.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, lb);
                dc.DrawText(ft, new Point(plot.Left - ft.Width - 8, y - ft.Height / 2));
            }

        if (XTicks != null) foreach (var t in XTicks)
            {
                double x = ox + (double)t * sx;
                if (x < plot.Left - 10 || x > plot.Right + 10) continue;

                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var ft = new FormattedText(t.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, lb);
                dc.DrawText(ft, new Point(x - ft.Width / 2, plot.Bottom + 6));
            }
    }

    private void EnsureSeriesCache(Rect plot, (int xmin, int xmax, byte ymin, byte ymax) range, double sx, double sy, double ox, double oy)
    {
        foreach (var s in Series)
        {
            if (!_seriesCache.TryGetValue(s.Id, out var cache)) _seriesCache[s.Id] = cache = new SeriesCache();
            bool mappingChanged = !cache.LastPlot.Equals(plot) || !cache.LastRange.Equals(range);

            if (cache.LastVersion == s.Version && !mappingChanged) continue;

            int count = s.Points.Count;
            if (count == 0) { cache.ReturnPool(); continue; }

            if (cache.PooledPixelPoints == null || cache.PooledPixelPoints.Length < count)
            { cache.ReturnPool(); cache.PooledPixelPoints = ArrayPool<Point>.Shared.Rent(count); }

            var lineGeom = new StreamGeometry();
            var markerGeom = new StreamGeometry();
            var errorGeom = s.ShowErrorBars ? new StreamGeometry() : null;

            using (var lCtx = lineGeom.Open())
            using (var mCtx = markerGeom.Open())
            using (var eCtx = errorGeom?.Open())
            {
                double lastX = -9999;

                for (int i = 0; i < count; i++)
                {
                    var p = s.Points[i];
                    var pp = new Point(ox + (double)p.Wavelength * sx, oy - (double)p.Mean * sy);
                    cache.PooledPixelPoints[i] = pp;

                    if (i > 0 && i < count - 1 && Math.Abs(pp.X - lastX) < 0.5) continue;
                    lastX = pp.X;

                    if (i == 0) lCtx.BeginFigure(pp, false); else lCtx.LineTo(pp);

                    double mR = s.MarkerRadius;
                    mCtx.BeginFigure(new Point(pp.X - mR, pp.Y), true);
                    mCtx.ArcTo(new Point(pp.X + mR, pp.Y), new Size(mR, mR), 0, false, SweepDirection.Clockwise);
                    mCtx.ArcTo(new Point(pp.X - mR, pp.Y), new Size(mR, mR), 0, false, SweepDirection.Clockwise);

                    if (eCtx != null && p.StdDev.HasValue)
                    {
                        double y0 = oy - (double)(p.Mean - p.StdDev.Value) * sy;
                        double y1 = oy - (double)(p.Mean + p.StdDev.Value) * sy;
                        eCtx.BeginFigure(new Point(pp.X, y0), false); eCtx.LineTo(new Point(pp.X, y1));
                        eCtx.BeginFigure(new Point(pp.X - s.ErrorCapHalfWidth, y0), false); eCtx.LineTo(new Point(pp.X + s.ErrorCapHalfWidth, y0));
                        eCtx.BeginFigure(new Point(pp.X - s.ErrorCapHalfWidth, y1), false); eCtx.LineTo(new Point(pp.X + s.ErrorCapHalfWidth, y1));
                    }
                }
            }
            cache.LineGeom = lineGeom; cache.MarkerGeom = markerGeom; cache.ErrorBarGeom = errorGeom;
            cache.PixelCount = count; cache.LastVersion = s.Version; cache.LastPlot = plot; cache.LastRange = range;
        }
    }

    private void OnPointerMovedInternal(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var plot = Bounds.Deflate(PlotPadding);

        if (plot.Contains(pos)) _lastMousePosition = pos;
        else _lastMousePosition = null;

        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _graphLayerBitmap?.Dispose();
        foreach (var kv in _seriesCache) kv.Value.ReturnPool();
        _seriesCache.Clear();
    }
}