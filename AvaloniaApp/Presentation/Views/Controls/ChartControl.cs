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

// 1. 데이터 모델 정의 (int 및 byte로 최적화)
public readonly record struct ChartPoint(int Wavelength, byte Mean, byte? StdDev = null);

public sealed class ChartSeries
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; init; } = "시리즈";

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

// 2. 커스텀 컨트롤 정의
public sealed class ChartControl : Control
{
    // --- 의존성 속성 (정수/바이트 타입 적용) ---
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
            new byte[] { 0, 51, 102, 153, 204, 255 });
    public IReadOnlyList<byte> YTicks { get => GetValue(YTicksProperty); set => SetValue(YTicksProperty, value); }

    public static readonly StyledProperty<bool> AutoScaleProperty = AvaloniaProperty.Register<ChartControl, bool>(nameof(AutoScale), true);
    public bool AutoScale { get => GetValue(AutoScaleProperty); set => SetValue(AutoScaleProperty, value); }

    public static readonly StyledProperty<double> RangePaddingProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(RangePadding), 0.1);
    public double RangePadding { get => GetValue(RangePaddingProperty); set => SetValue(RangePaddingProperty, value); }

    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChartControl, IBrush>(nameof(BackgroundBrush), new SolidColorBrush(Color.Parse("#0C111A")));
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }

    public static readonly Thickness PlotPadding = new Thickness(50, 20, 20, 40);

    // --- 내부 캐싱 ---
    private bool _staticDirty = true;
    private RenderTargetBitmap? _staticLayer;
    private PixelSize _lastPixelSize;
    private double _lastScaling;

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

    private readonly Dictionary<string, SeriesCache> _seriesCache = new();
    private FormattedText? _hoverFormatted;
    private Point _hoverPixelPos;

    public ChartControl()
    {
        Series = new ObservableCollection<ChartSeries>();
        AttachCollection(Series);
        ClipToBounds = true;
        PointerMoved += OnPointerMovedInternal;
        PointerExited += (_, __) => { _hoverFormatted = null; InvalidateVisual(); };
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

    private void OnSeriesDataChanged(object? sender, EventArgs e) { if (AutoScale) UpdateAutoScale(); InvalidateVisual(); }
    private void OnSeriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (ChartSeries s in e.NewItems) s.Changed += OnSeriesDataChanged;
        if (e.OldItems != null) foreach (ChartSeries s in e.OldItems) { s.Changed -= OnSeriesDataChanged; if (_seriesCache.TryGetValue(s.Id, out var c)) c.ReturnPool(); _seriesCache.Remove(s.Id); }
        if (AutoScale) UpdateAutoScale();
        _staticDirty = true; InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeriesProperty) { DetachCollection(change.OldValue as ObservableCollection<ChartSeries>); AttachCollection(change.NewValue as ObservableCollection<ChartSeries>); _staticDirty = true; }
        else if (change.Property == XMinProperty || change.Property == XMaxProperty || change.Property == YMinProperty || change.Property == YMaxProperty || change.Property == XTicksProperty || change.Property == YTicksProperty) _staticDirty = true;
        InvalidateVisual();
    }

    private void UpdateAutoScale()
    {
        if (Series == null || Series.Count == 0) return;
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = 255, maxY = 0;
        bool hasData = false;

        foreach (var s in Series)
        {
            if (s.Points.Count == 0) continue;
            hasData = true;
            foreach (var p in s.Points)
            {
                minX = Math.Min(minX, p.Wavelength); maxX = Math.Max(maxX, p.Wavelength);
                int yLow = p.Mean - (p.StdDev ?? 0); int yHigh = p.Mean + (p.StdDev ?? 0);
                minY = Math.Min(minY, Math.Max(0, yLow)); maxY = Math.Max(maxY, Math.Min(255, yHigh));
            }
        }
        if (!hasData) return;

        double pad = RangePadding;
        int dx = maxX - minX; if (dx == 0) dx = 1;
        int dy = maxY - minY; if (dy == 0) dy = 1;

        SetCurrentValue(XMinProperty, (int)(minX - dx * pad));
        SetCurrentValue(XMaxProperty, (int)(maxX + dx * pad));
        SetCurrentValue(YMinProperty, (byte)Math.Clamp(minY - dy * pad, 0, 255));
        SetCurrentValue(YMaxProperty, (byte)Math.Clamp(maxY + dy * pad, 0, 255));
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var plot = bounds.Deflate(PlotPadding);
        var range = (xmin: XMin, xmax: XMax, ymin: YMin, ymax: YMax);

        EnsureStaticLayer(bounds, plot, range);
        if (_staticLayer != null) context.DrawImage(_staticLayer, new Rect(0, 0, _staticLayer.PixelSize.Width, _staticLayer.PixelSize.Height), bounds);

        if (range.xmax <= range.xmin || range.ymax <= range.ymin) return;
        double sx = plot.Width / (range.xmax - range.xmin);
        double sy = plot.Height / (range.ymax - range.ymin);
        double ox = plot.Left - (double)range.xmin * sx;
        double oy = plot.Bottom + (double)range.ymin * sy;

        using (context.PushClip(plot))
        {
            EnsureSeriesCache(plot, range, sx, sy, ox, oy);
            foreach (var s in Series)
            {
                if (!_seriesCache.TryGetValue(s.Id, out var cache)) continue;
                if (cache.LineGeom != null) context.DrawGeometry(null, s.LinePen, cache.LineGeom);
                if (s.ShowErrorBars && cache.ErrorBarGeom != null) context.DrawGeometry(null, s.LinePen, cache.ErrorBarGeom);
                if (cache.MarkerGeom != null) context.DrawGeometry(s.MarkerFill, null, cache.MarkerGeom);
            }
        }

        if (_hoverFormatted != null)
        {
            var rect = new Rect(_hoverPixelPos.X + 12, _hoverPixelPos.Y - _hoverFormatted.Height - 12, _hoverFormatted.Width + 10, _hoverFormatted.Height + 6);
            if (rect.Right > bounds.Right) rect = rect.WithX(_hoverPixelPos.X - rect.Width - 12);
            context.FillRectangle(new SolidColorBrush(Color.Parse("#E6222222")), rect, 4);
            context.DrawText(_hoverFormatted, new Point(rect.X + 5, rect.Y + 3));
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
                for (int i = 0; i < count; i++)
                {
                    var p = s.Points[i];
                    var pp = new Point(ox + (double)p.Wavelength * sx, oy - (double)p.Mean * sy);
                    cache.PooledPixelPoints[i] = pp;

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

    private void EnsureStaticLayer(Rect bounds, Rect plot, (int xmin, int xmax, byte ymin, byte ymax) range)
    {
        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        var pixelSize = PixelSize.FromSize(bounds.Size, scaling);
        if (_staticLayer == null || _lastPixelSize != pixelSize || _lastScaling != scaling || _staticDirty)
        {
            _staticLayer?.Dispose();
            _staticLayer = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
            _lastPixelSize = pixelSize; _lastScaling = scaling; _staticDirty = false;
            using var dc = _staticLayer.CreateDrawingContext();
            dc.FillRectangle(BackgroundBrush, bounds);
            dc.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#33FFFFFF")), 1), plot);

            double sx = plot.Width / (range.xmax - range.xmin);
            double sy = plot.Height / (range.ymax - range.ymin);
            double ox = plot.Left - (double)range.xmin * sx;
            double oy = plot.Bottom + (double)range.ymin * sy;

            var gridPen = new Pen(new SolidColorBrush(Color.Parse("#12FFFFFF")), 1);
            var lb = new SolidColorBrush(Color.Parse("#99FFFFFF"));
            var tf = new Typeface(FontFamily.Default);

            if (YTicks != null) foreach (var t in YTicks)
                {
                    double y = oy - (double)t * sy;
                    dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                    var ft = new FormattedText(t.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, lb);
                    dc.DrawText(ft, new Point(plot.Left - ft.Width - 8, y - ft.Height / 2));
                }

            if (XTicks != null) foreach (var t in XTicks)
                {
                    double x = ox + (double)t * sx;
                    dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                    var ft = new FormattedText(t.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, lb);
                    dc.DrawText(ft, new Point(x - ft.Width / 2, plot.Bottom + 6));
                }
        }
    }

    private void OnPointerMovedInternal(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var plot = Bounds.Deflate(PlotPadding);
        if (!plot.Contains(pos)) { _hoverFormatted = null; InvalidateVisual(); return; }
        double bestD2 = 625; string? bestText = null; Point bestPos = default;
        foreach (var s in Series)
        {
            if (!_seriesCache.TryGetValue(s.Id, out var cache) || cache.PooledPixelPoints == null) continue;
            for (int i = 0; i < cache.PixelCount; i++)
            {
                var pix = cache.PooledPixelPoints[i];
                double d2 = (pix.X - pos.X) * (pix.X - pos.X) + (pix.Y - pos.Y) * (pix.Y - pos.Y);
                if (d2 < bestD2)
                {
                    bestD2 = d2; bestPos = pix;
                    bestText = $"{s.DisplayName}\nMean : {s.Points[i].Mean}\nStdDev : {s.Points[i].StdDev}";
                }
            }
        }
        if (bestText != null)
        {
            _hoverFormatted = new FormattedText(bestText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
            _hoverPixelPos = bestPos;
        }
        else _hoverFormatted = null;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _staticLayer?.Dispose();
        foreach (var kv in _seriesCache) kv.Value.ReturnPool();
        _seriesCache.Clear();
    }
}

// 데모 데이터 생성 헬퍼
public static class ScreenshotChartDemo
{
    public static ObservableCollection<ChartSeries> CreateSeriesLikeScreenshot()
    {
        var red = new List<ChartPoint> { new(410, 25, 6), new(430, 45, 7), new(450, 35, 6), new(470, 30, 6), new(490, 85, 8), new(510, 50, 7), new(530, 45, 6), new(550, 40, 6), new(570, 35, 6), new(590, 45, 7), new(610, 120, 10), new(630, 145, 12), new(650, 155, 12), new(670, 140, 10), new(690, 140, 10) };
        var green = new List<ChartPoint> { new(410, 20), new(430, 35), new(450, 30), new(470, 25), new(490, 90), new(510, 110), new(530, 95), new(550, 70), new(570, 55), new(590, 40), new(610, 35), new(630, 38), new(650, 36), new(670, 34), new(690, 65) };
        var blue = new List<ChartPoint> { new(410, 30), new(430, 85), new(450, 95), new(470, 90), new(490, 92), new(510, 60), new(530, 40), new(550, 30), new(570, 25), new(590, 20), new(610, 18), new(630, 25), new(650, 22), new(670, 24), new(690, 45) };

        return new ObservableCollection<ChartSeries>
        {
            new ChartSeries { DisplayName = "A", Points = red, LinePen = new Pen(Brushes.Crimson, 2), MarkerFill = Brushes.Crimson, ShowErrorBars = true },
            new ChartSeries { DisplayName = "B", Points = green, LinePen = new Pen(Brushes.SeaGreen, 2), MarkerFill = Brushes.SeaGreen },
            new ChartSeries { DisplayName = "C", Points = blue, LinePen = new Pen(Brushes.DodgerBlue, 2), MarkerFill = Brushes.DodgerBlue }
        };
    }
}