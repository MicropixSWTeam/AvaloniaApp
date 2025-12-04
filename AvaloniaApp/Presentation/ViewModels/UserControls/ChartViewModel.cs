// AvaloniaApp.Presentation/ViewModels/UserControls/ChartViewModel.cs
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
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

        [ObservableProperty]
        private SelectionRegion? selectedRegion;

        private readonly RegionAnalysisWorkspace _analysis;

        public string Title { get; set; } = "Spectrum Chart";
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 400;

        public IEnumerable<SelectionRegion> Regions => _analysis.Regions;

        // ROI 색상 팔레트 (차트 + 카메라 ROI에 같이 쓸 수 있도록 고정 팔레트)
        private static readonly SKColor[] Palette =
        {
            new SKColor( 59, 130, 246), // 파랑
            new SKColor( 34, 197,  94), // 초록
            new SKColor(239,  68,  68), // 레드
            new SKColor(234, 179,   8), // 노랑
            new SKColor( 56, 189, 248), // 시안
            new SKColor(168,  85, 247), // 보라
        };

        public ChartViewModel(RegionAnalysisWorkspace analysis)
        {
            _analysis = analysis;
            _analysis.Changed += (_, __) => RebuildSeries();

            // X축: 15개 밴드 (410~690nm), 항상 전부 보이도록 MinStep = 1
            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Wavelength [nm]",
                    NamePaint = new SolidColorPaint(SKColors.LightGray),
                    NameTextSize = 16,
                    NamePadding = new LiveChartsCore.Drawing.Padding(0, 10, 0, 0),

                    Labels = new[]
                    {
                        "410","430","450","470","490",
                        "510","530","550","570","590",
                        "610","630","650","670","690"
                    },
                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 13,

                    // 카테고리 축에서 모든 tick 을 보이게
                    MinStep = 1,

                    // 눈금/축선 색
                    SeparatorsPaint = new SolidColorPaint(new SKColor(60, 72, 88)),
                    TicksPaint       = new SolidColorPaint(new SKColor(60, 72, 88)),
                    ShowSeparatorLines = true
                }
            };

            // Y축: Intensity (0~255 고정)
            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Intensity",
                    NamePaint = new SolidColorPaint(SKColors.LightGray),
                    NameTextSize = 16,
                    NamePadding = new LiveChartsCore.Drawing.Padding(0, 0, 10, 0),

                    MinLimit = 0,
                    MaxLimit = 255,
                    MinStep  = 51,
                    Labeler  = value => value.ToString("0"),

                    LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                    TextSize = 13,

                    SeparatorsPaint = new SolidColorPaint(new SKColor(60, 72, 88)),
                    TicksPaint       = new SolidColorPaint(new SKColor(60, 72, 88)),
                    ShowSeparatorLines = true
                }
            };

            Series = Array.Empty<ISeries>();
        }

        private void RebuildSeries()
        {
            if (_analysis.Regions.Count == 0 || _analysis.RegionTileStats.Count == 0)
            {
                Series = Array.Empty<ISeries>();
                return;
            }

            var list = new List<ISeries>();

            for (int i = 0; i < _analysis.Regions.Count; i++)
            {
                var region = _analysis.Regions[i];

                if (!_analysis.RegionTileStats.TryGetValue(region.Index, out var stats))
                    continue;

                // ErrorValue: Value = mean, Error = half std
                var values = stats
                    .Select(ts =>
                    {
                        var halfStd = ts.StdDev / 2.0;
                        return new ErrorValue(ts.Mean, halfStd);
                    })
                    .ToArray();

                var color = Palette[i % Palette.Length];

                var line = new LineSeries<ErrorValue>
                {
                    Name = $"ROI {region.Index}",
                    Values = values,

                    // 선 스타일
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    Fill = null,
                    LineSmoothness = 0, // 직선

                    // 포인트(동그라미)
                    GeometrySize = 8,
                    GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    GeometryFill = new SolidColorPaint(new SKColor(15, 23, 42)), // 어두운 배경색 정도

                    // 에러바 (표준편차의 절반만 위/아래로)
                    ErrorPaint = new SolidColorPaint(color) { StrokeThickness = 1 },

                    XToolTipLabelFormatter = null,
                    YToolTipLabelFormatter = null
                };

                list.Add(line);
            }

            Series = list.ToArray();
        }

        [RelayCommand]
        private void ClearRegions()
        {
            _analysis.Clear();
            SelectedRegion = null;
        }

        [RelayCommand]
        private void RemoveSelectedRegion()
        {
            if (SelectedRegion is null) return;
            _analysis.RemoveRegion(SelectedRegion);
            SelectedRegion = null;
        }
    }
}
