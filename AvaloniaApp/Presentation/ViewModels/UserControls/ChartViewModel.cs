// ===== AvaloniaApp.Presentation/ViewModels/UserControls/ChartViewModel.cs (체크박스 + 색상 연동 버전) =====
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class ChartViewModel : ViewModelBase, IPopup
    {
        [ObservableProperty]
        private ISeries[]? series;

        [ObservableProperty]
        private Axis[]? xAxes;

        [ObservableProperty]
        private Axis[]? yAxes;

        /// <summary>
        /// ROI 리스트 + 체크박스용 항목.
        /// </summary>
        public ObservableCollection<RegionCheckItem> RegionItems { get; } = new();

        [ObservableProperty]
        private RegionCheckItem? selectedRegionItem;

        private readonly RegionAnalysisWorkspace _analysis;
        private readonly ImageProcessService _imageProcessService;
        /// <summary>
        /// 차트에 동시에 표시할 최대 ROI 개수 (최근 N개).
        /// </summary>
        public int MaxRegionsInChart { get; set; } = 6;

        public string Title { get; set; } = "Spectrum Chart";
        public int Width { get; set; } = 900;
        public int Height { get; set; } = 600;

        /// <summary>
        /// 다른 View(XAML 등)에서 필요하면 직접 Regions에 접근할 수 있도록 노출.
        /// </summary>
        public IEnumerable<SelectionRegion> Regions => _analysis.Regions;

        public ChartViewModel(RegionAnalysisWorkspace analysis,ImageProcessService imageProcessService)
        {
            _analysis = analysis;
            _imageProcessService = imageProcessService;

            _analysis.Changed += (_, __) =>
            {
                SyncRegionItems();
                RebuildSeries();
            };

            var wavelengthLabels = new[]
            {
                "410","430","450","470","490",
                "510","530","550","570","590",
                "610","630","650","670","690"
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Wavelength [nm]",
                    NamePaint = new SolidColorPaint(SKColors.LightGray),
                    NameTextSize = 16,
                    NamePadding = new LiveChartsCore.Drawing.Padding(0, 10, 0, 0),

                    // 기존 라벨 그대로
                    Labels = wavelengthLabels,
                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 13,

                    MinStep = 1,

                    // ★ 좌우로 살짝 여유를 줘서 그래프가 박스 안에 들어온 느낌
                    //   포인트 인덱스가 0 ~ 14 라고 보면,
                    //   -0.5 ~ 14.5 로 범위를 넓혀서 좌우에 margin 을 만듦.
                    MinLimit = -0.5,
                    MaxLimit = wavelengthLabels.Length - 0.5,

                    // ★ 내부 격자선/틱 제거 (지저분한 검은선 없애기)
                    SeparatorsPaint = null,
                    TicksPaint = null,
                    ShowSeparatorLines = false
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Intensity",
                    NamePaint = new SolidColorPaint(SKColors.LightGray),
                    NameTextSize = 16,
                    NamePadding = new LiveChartsCore.Drawing.Padding(0, 0, 10, 0),

                    MinLimit = -50,
                    MaxLimit = 300,
                    MinStep  = 50,
                    Labeler  = value => value.ToString("0"),

                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 13,

                    // ★ 내부 가로 격자선/틱 모두 제거
                    SeparatorsPaint = null,
                    TicksPaint = null,
                    ShowSeparatorLines = false
                }
            };


            Series = Array.Empty<ISeries>();

            // 초기 동기화
            SyncRegionItems();
            RebuildSeries();
        }

        partial void OnSelectedRegionItemChanged(RegionCheckItem? value)
        {
            // 필요시 선택 변경에 따른 추가 동작 가능
        }

        private void SyncRegionItems()
        {
            // 기존 체크 상태 보존
            var checkedMap = RegionItems.ToDictionary(i => i.Region.Index, i => i.IsChecked);

            foreach (var item in RegionItems)
            {
                item.PropertyChanged -= RegionItemOnPropertyChanged;
            }

            RegionItems.Clear();

            foreach (var region in _analysis.Regions)
            {
                var item = new RegionCheckItem(region);
                if (checkedMap.TryGetValue(region.Index, out var isChecked))
                    item.IsChecked = isChecked;

                item.PropertyChanged += RegionItemOnPropertyChanged;
                RegionItems.Add(item);
            }

            if (SelectedRegionItem is not null && !RegionItems.Contains(SelectedRegionItem))
            {
                SelectedRegionItem = null;
            }
        }

        private void RegionItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RegionCheckItem.IsChecked))
            {
                RebuildSeries();
            }
        }
        private void RebuildSeries()
        {
            if (_analysis.Regions.Count == 0 || _analysis.RegionTileStats.Count == 0)
            {
                Series = Array.Empty<ISeries>();
                return;
            }

            // 체크된 ROI들만 대상으로
            var checkedRegions = RegionItems
                .Where(i => i.IsChecked)
                .Select(i => i.Region)
                .ToList();

            if (checkedRegions.Count == 0)
            {
                Series = Array.Empty<ISeries>();
                return;
            }

            // 너무 많으면 뒤에서 MaxRegionsInChart 개만 남김 (최근 ROI 위주)
            if (checkedRegions.Count > MaxRegionsInChart)
            {
                checkedRegions = checkedRegions
                    .Skip(checkedRegions.Count - MaxRegionsInChart)
                    .ToList();
            }

            var list = new List<ISeries>();

            // ★ 타일 순서를 "아래 행 → 위 행"으로 가져오기
            var order = _imageProcessService
                .GetTileIndicesBottomToTop()
                .ToArray();   // 예: 10 11 12 13 14, 5 6 7 8 9, 0 1 2 3 4

            foreach (var region in checkedRegions)
            {
                if (!_analysis.RegionTileStats.TryGetValue(region.Index, out var stats))
                    continue;

                // stats: grid 기준 순서(0..TileCount-1)
                // order 순서대로 재정렬
                var orderedValues = new List<ErrorValue>(order.Length);
                var orderedMeans = new List<double>(order.Length);
                var orderedStds = new List<double>(order.Length);

                foreach (var idx in order)
                {
                    if (idx < 0 || idx >= stats.Count)
                        continue;

                    var ts = stats[idx];
                    var m = ts.Mean;
                    var std = ts.StdDev;

                    orderedMeans.Add(m);
                    orderedStds.Add(std);

                    var halfStd = std / 2.0;
                    orderedValues.Add(new ErrorValue(m, halfStd));
                }

                var means = orderedMeans.ToArray();
                var stds = orderedStds.ToArray();

                var color = RegionColorPalette.GetSkColor(region.ColorIndex);

                var line = new LineSeries<ErrorValue>
                {
                    Name = $"ROI {region.Index}",
                    Values = orderedValues.ToArray(),

                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2.5f }, // 살짝 더 두껍게
                    Fill = null, // 배경 채우기 없이 라인만 선명하게
                    LineSmoothness = 0, // 스펙트럼은 직선 느낌이 좋아서 0 유지

                    GeometrySize = 6, // 8 → 6 으로 줄여서 덜 답답하게
                    GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    GeometryFill = new SolidColorPaint(new SKColor(15, 23, 42)),

                    ErrorPaint = new SolidColorPaint(color) { StrokeThickness = 1 },

                    YToolTipLabelFormatter = point =>
                    {
                        var idx = point.Index;
                        if (idx < 0 || idx >= means.Length)
                            return string.Empty;

                        var mean = means[idx];
                        var std = stds[idx];

                        return $"Mean = {mean:F2}, Std = {std:F2}";
                    }
                };


                list.Add(line);
            }

            Series = list.ToArray();
        }


        [RelayCommand]
        private void ClearRegions()
        {
            _analysis.Clear();
            RegionItems.Clear();
            Series = Array.Empty<ISeries>();
        }

        /// <summary>
        /// 체크된 ROI들을 모두 제거.
        /// </summary>
        [RelayCommand]
        private void RemoveCheckedRegions()
        {
            var toRemove = RegionItems
                .Where(i => i.IsChecked)
                .Select(i => i.Region)
                .ToList();

            if (toRemove.Count == 0)
                return;

            foreach (var region in toRemove)
            {
                _analysis.RemoveRegion(region);
            }
        }
    }
    /// <summary>
    /// ChartView에서 ROI 리스트를 표시하기 위한 체크박스용 ViewModel 항목.
    /// </summary>
    public partial class RegionCheckItem : ObservableObject
    {
        public SelectionRegion Region { get; }


        [ObservableProperty]
        private bool isChecked = true; // 기본값: 차트에 표시


        public int Index => Region.Index;
        public double Mean => Region.Mean;
        public double StdDev => Region.StdDev;


        public RegionCheckItem(SelectionRegion region)
        {
            Region = region;
        }
    }
}