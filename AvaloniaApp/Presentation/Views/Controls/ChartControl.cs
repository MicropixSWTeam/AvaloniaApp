using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;

namespace AvaloniaApp.Presentation.Views.Controls;

// 1. 데이터 모델 정의
public readonly record struct ChartPoint(double X, double Y, double? YError = null);

public sealed class ChartSeries
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<ChartPoint> Points { get; set; } = Array.Empty<ChartPoint>();

    public IPen LinePen { get; init; } = new Pen(Brushes.White, 2);
    public IBrush MarkerFill { get; init; } = Brushes.White;
    public double MarkerRadius { get; init; } = 3;

    public bool ShowErrorBars { get; init; } = false;
    public double ErrorCapHalfWidth { get; init; } = 5;
}

// 2. 커스텀 컨트롤 정의
public sealed class ChartControl : Control
{
    // --- 의존성 속성 (Styled Properties) ---
    public static readonly StyledProperty<ObservableCollection<ChartSeries>> SeriesProperty =
        AvaloniaProperty.Register<ChartControl, ObservableCollection<ChartSeries>>(nameof(Series));

    public ObservableCollection<ChartSeries> Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    // X축 눈금 (410 ~ 690, 20단위)
    public static readonly StyledProperty<IReadOnlyList<double>> XTicksProperty =
        AvaloniaProperty.Register<ChartControl, IReadOnlyList<double>>(nameof(XTicks),
            new double[] { 410, 430, 450, 470, 490, 510, 530, 550, 570, 590, 610, 630, 650, 670, 690 });

    public IReadOnlyList<double> XTicks
    {
        get => GetValue(XTicksProperty);
        set => SetValue(XTicksProperty, value);
    }

    // Y축 눈금 (51단위)
    public static readonly StyledProperty<IReadOnlyList<double>> YTicksProperty =
        AvaloniaProperty.Register<ChartControl, IReadOnlyList<double>>(nameof(YTicks),
            new double[] { 51, 102, 153, 204, 255 });

    public IReadOnlyList<double> YTicks
    {
        get => GetValue(YTicksProperty);
        set => SetValue(YTicksProperty, value);
    }

    // 축 범위
    public static readonly StyledProperty<double> XMinProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(XMin), 410);
    public static readonly StyledProperty<double> XMaxProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(XMax), 690);
    public static readonly StyledProperty<double> YMinProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(YMin), 0);
    public static readonly StyledProperty<double> YMaxProperty = AvaloniaProperty.Register<ChartControl, double>(nameof(YMax), 255);

    public double XMin { get => GetValue(XMinProperty); set => SetValue(XMinProperty, value); }
    public double XMax { get => GetValue(XMaxProperty); set => SetValue(XMaxProperty, value); }
    public double YMin { get => GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public double YMax { get => GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }

    // 배경 그라데이션
    public static readonly StyledProperty<IBrush> BackgroundBrushProperty =
        AvaloniaProperty.Register<ChartControl, IBrush>(nameof(BackgroundBrush),
            new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse("#0C111A"), 0),
                    new GradientStop(Color.Parse("#060912"), 1)
                }
            });
    public IBrush BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }

    // 테두리 및 라벨 스타일
    public static readonly StyledProperty<IPen> PlotBorderPenProperty =
        AvaloniaProperty.Register<ChartControl, IPen>(nameof(PlotBorderPen), new Pen(Brushes.White, 1));
    public IPen PlotBorderPen { get => GetValue(PlotBorderPenProperty); set => SetValue(PlotBorderPenProperty, value); }

    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<ChartControl, IBrush>(nameof(LabelBrush), new SolidColorBrush(Color.Parse("#D6DCE6")));
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<ChartControl, double>(nameof(LabelFontSize), 12);
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    public static readonly StyledProperty<Thickness> PlotPaddingProperty =
        AvaloniaProperty.Register<ChartControl, Thickness>(nameof(PlotPadding), new Thickness(46, 14, 10, 34));
    public Thickness PlotPadding { get => GetValue(PlotPaddingProperty); set => SetValue(PlotPaddingProperty, value); }

    // --- 내부 캐싱 및 렌더링 로직 ---
    private bool _dirty = true;
    private Rect _lastPlot;
    private (double xmin, double xmax, double ymin, double ymax) _lastRange;

    private readonly Dictionary<double, FormattedText> _xLabelCache = new();
    private readonly Dictionary<double, FormattedText> _yLabelCache = new();
    private double _cacheFontSize;
    private IBrush? _cacheBrush;
    private IReadOnlyList<double>? _cacheXTicks;
    private IReadOnlyList<double>? _cacheYTicks;

    private sealed class SeriesCache
    {
        public StreamGeometry? LineGeom;
        public Point[]? PixelPoints;
    }
    private readonly Dictionary<string, SeriesCache> _seriesCache = new();

    public ChartControl()
    {
        Series = new ObservableCollection<ChartSeries>();
        Series.CollectionChanged += OnSeriesCollectionChanged;
        ClipToBounds = true;
    }

    private void OnSeriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _dirty = true;
        _seriesCache.Clear();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SeriesProperty)
        {
            if (change.OldValue is ObservableCollection<ChartSeries> oldCol) oldCol.CollectionChanged -= OnSeriesCollectionChanged;
            if (change.NewValue is ObservableCollection<ChartSeries> newCol) newCol.CollectionChanged += OnSeriesCollectionChanged;
            _dirty = true;
            _seriesCache.Clear();
            InvalidateVisual();
        }

        if (change.Property == XMinProperty || change.Property == XMaxProperty ||
            change.Property == YMinProperty || change.Property == YMaxProperty ||
            change.Property == PlotPaddingProperty || change.Property == PlotBorderPenProperty ||
            change.Property == BackgroundBrushProperty || change.Property == XTicksProperty ||
            change.Property == YTicksProperty || change.Property == LabelBrushProperty ||
            change.Property == LabelFontSizeProperty)
        {
            _dirty = true;
            InvalidateVisual();
        }
    }

    public void NotifyDataChanged()
    {
        _dirty = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        context.FillRectangle(BackgroundBrush, bounds);

        var plot = bounds.Deflate(PlotPadding);
        if (plot.Width <= 1 || plot.Height <= 1) return;

        var range = (xmin: XMin, xmax: XMax, ymin: YMin, ymax: YMax);
        if (Math.Abs(range.xmax - range.xmin) < 1e-9 || Math.Abs(range.ymax - range.ymin) < 1e-9) return;

        context.DrawRectangle(PlotBorderPen, plot);
        EnsureLabelCache();

        double MapX(double xVal) => plot.Left + (xVal - range.xmin) / (range.xmax - range.xmin) * plot.Width;
        double MapY(double yVal) => plot.Bottom - (yVal - range.ymin) / (range.ymax - range.ymin) * plot.Height;

        // Y축 라벨 그리기
        if (YTicks is { Count: > 0 })
        {
            foreach (var t in YTicks)
            {
                var y = MapY(t);
                if (_yLabelCache.TryGetValue(t, out var ft))
                    context.DrawText(ft, new Point(plot.Left - 8 - ft.Width, y - ft.Height / 2));
            }
        }

        // X축 라벨 그리기
        if (XTicks is { Count: > 0 })
        {
            foreach (var t in XTicks)
            {
                var x = MapX(t);
                if (_xLabelCache.TryGetValue(t, out var ft))
                    context.DrawText(ft, new Point(x - ft.Width / 2, plot.Bottom + 6));
            }
        }

        // 데이터 시리즈 그리기 (클리핑 적용)
        using (context.PushClip(plot))
        {
            EnsureSeriesCache(plot, range);

            foreach (var s in Series)
            {
                if (!_seriesCache.TryGetValue(s.Id, out var cache) || cache.PixelPoints is null) continue;

                if (cache.LineGeom is not null)
                    context.DrawGeometry(null, s.LinePen, cache.LineGeom);

                var pts = s.Points;
                var pix = cache.PixelPoints;

                for (int i = 0; i < pix.Length; i++)
                {
                    var p = pts[i];
                    var pp = pix[i];

                    // 에러바
                    if (s.ShowErrorBars && p.YError is double err && err > 0)
                    {
                        var y0 = MapY(p.Y - err);
                        var y1 = MapY(p.Y + err);
                        var cap = s.ErrorCapHalfWidth;

                        context.DrawLine(s.LinePen, new Point(pp.X, y0), new Point(pp.X, y1));
                        context.DrawLine(s.LinePen, new Point(pp.X - cap, y0), new Point(pp.X + cap, y0));
                        context.DrawLine(s.LinePen, new Point(pp.X - cap, y1), new Point(pp.X + cap, y1));
                    }
                    // 마커
                    context.DrawEllipse(s.MarkerFill, null, pp, s.MarkerRadius, s.MarkerRadius);
                }
            }
        }
    }

    private void EnsureLabelCache()
    {
        var needRebuild = _cacheBrush != LabelBrush || Math.Abs(_cacheFontSize - LabelFontSize) > 1e-6 ||
                          !ReferenceEquals(_cacheXTicks, XTicks) || !ReferenceEquals(_cacheYTicks, YTicks);

        if (!needRebuild && _xLabelCache.Count > 0 && _yLabelCache.Count > 0) return;

        _xLabelCache.Clear();
        _yLabelCache.Clear();

        _cacheBrush = LabelBrush;
        _cacheFontSize = LabelFontSize;
        _cacheXTicks = XTicks;
        _cacheYTicks = YTicks;

        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

        if (XTicks is { Count: > 0 })
        {
            foreach (var x in XTicks)
                _xLabelCache[x] = new FormattedText(x.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, LabelFontSize, LabelBrush);
        }
        if (YTicks is { Count: > 0 })
        {
            foreach (var y in YTicks)
                _yLabelCache[y] = new FormattedText(y.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, LabelFontSize, LabelBrush);
        }
    }

    private void EnsureSeriesCache(Rect plot, (double xmin, double xmax, double ymin, double ymax) range)
    {
        if (!_dirty && plot.Equals(_lastPlot) && range == _lastRange) return;
        _dirty = false;
        _lastPlot = plot;
        _lastRange = range;

        double MapX(double xVal) => plot.Left + (xVal - range.xmin) / (range.xmax - range.xmin) * plot.Width;
        double MapY(double yVal) => plot.Bottom - (yVal - range.ymin) / (range.ymax - range.ymin) * plot.Height;

        var alive = new HashSet<string>(Series.Select(s => s.Id));
        foreach (var key in _seriesCache.Keys.ToArray()) if (!alive.Contains(key)) _seriesCache.Remove(key);

        foreach (var s in Series)
        {
            if (!_seriesCache.TryGetValue(s.Id, out var cache))
            {
                cache = new SeriesCache();
                _seriesCache[s.Id] = cache;
            }

            var pts = s.Points;
            if (pts == null || pts.Count < 2)
            {
                cache.LineGeom = null;
                cache.PixelPoints = null;
                continue;
            }

            var pix = new Point[pts.Count];
            for (int i = 0; i < pts.Count; i++) pix[i] = new Point(MapX(pts[i].X), MapY(pts[i].Y));
            cache.PixelPoints = pix;

            var geom = new StreamGeometry();
            using (var g = geom.Open())
            {
                g.BeginFigure(pix[0], isFilled: false);
                for (int i = 1; i < pix.Length; i++) g.LineTo(pix[i]);
            }
            cache.LineGeom = geom;
        }
    }
}

// 3. 샘플 데이터 생성 도우미 (데모용)
public static class ScreenshotChartDemo
{
    public static ObservableCollection<ChartSeries> CreateSeriesLikeScreenshot()
    {
        var red = new List<ChartPoint>
        {
            new(410, 25, 6), new(430, 45, 7), new(450, 35, 6), new(470, 30, 6), new(490, 85, 8),
            new(510, 50, 7), new(530, 45, 6), new(550, 40, 6), new(570, 35, 6), new(590, 45, 7),
            new(610, 120, 10), new(630, 145, 12), new(650, 155, 12), new(670, 140, 10), new(690, 140, 10)
        };

        var green = new List<ChartPoint>
        {
            new(410, 20), new(430, 35), new(450, 30), new(470, 25), new(490, 90),
            new(510, 110), new(530, 95), new(550, 70), new(570, 55), new(590, 40),
            new(610, 35), new(630, 38), new(650, 36), new(670, 34), new(690, 65)
        };

        var blue = new List<ChartPoint>
        {
            new(410, 30), new(430, 85), new(450, 95), new(470, 90), new(490, 92),
            new(510, 60), new(530, 40), new(550, 30), new(570, 25), new(590, 20),
            new(610, 18), new(630, 25), new(650, 22), new(670, 24), new(690, 45)
        };

        return new ObservableCollection<ChartSeries>
        {
            new ChartSeries
            {
                Id = "red", Points = red,
                LinePen = new Pen(new SolidColorBrush(Color.Parse("#FF3B30")), 2),
                MarkerFill = new SolidColorBrush(Color.Parse("#FF3B30")),
                MarkerRadius = 3, ShowErrorBars = true, ErrorCapHalfWidth = 5
            },
            new ChartSeries
            {
                Id = "green", Points = green,
                LinePen = new Pen(new SolidColorBrush(Color.Parse("#34C759")), 2),
                MarkerFill = new SolidColorBrush(Color.Parse("#34C759")), MarkerRadius = 3
            },
            new ChartSeries
            {
                Id = "blue", Points = blue,
                LinePen = new Pen(new SolidColorBrush(Color.Parse("#0A84FF")), 2),
                MarkerFill = new SolidColorBrush(Color.Parse("#0A84FF")), MarkerRadius = 3
            }
        };
    }
}