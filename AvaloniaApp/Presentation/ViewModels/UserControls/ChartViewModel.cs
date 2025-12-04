using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Presentation.ViewModels;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{

    public partial class ChartViewModel:ViewModelBase,IPopup    
    {
        [ObservableProperty]
        private ISeries[]? series;

        [ObservableProperty]
        private Axis[]? xAxes;

        [ObservableProperty]
        private Axis[]? yAxes;

        private ChartModel? _chartModel;
        private readonly List<SpectrumData> _spectrumDatas = new();

        public string Title { get; set; } = "Spectrum Chart";
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 400;
        public ChartViewModel()
        {
            // 예시 데이터: 평균 + 표준편차
            // x = 0,1,2,3 / mean, sd
            var means = new[] { 100,110,200,250,50, 100, 110, 200, 250, 50, 100, 110, 200, 250, 50 };
            var sds = new[] { 5, 8, 10, 6 ,2, 5, 8, 1, 6, 2 , 5, 8, 1, 6, 2 };

            var errorValues = means
                .Select((m, i) => new ErrorValue(m, sds[i])) // m ± sd
                .ToArray();

            Series = new ISeries[]
            {
                new LineSeries<ErrorValue>
                {
                    Name   = "Intensity",
                    Values = errorValues,
                    Fill = null,
                    LineSmoothness = 0,
                    // 라인/포인트 스타일
                    Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
                    // 에러 바 스타일 (선 굵기 등)
                    ErrorPaint = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
                }
            };
            XAxes = new Axis[]
            {
                new Axis
                {
                    // === X label (xlabel) 항상 표시되게 ===
                    Name = "Wavelength [nm]",                  // 축 제목
                    NamePaint = new SolidColorPaint(SKColors.Black), // 제목 그릴 펜(이게 없으면 제목이 안 나올 수 있음)
                    NameTextSize = 16,                         // 제목 폰트 크기
                    NamePadding = new Padding(0, 10, 0, 0),    // 축과 제목 사이 여백
                   
                    TextSize = 14,                             // 눈금 레이블 크기
                    Labels = new[]
                    {
                        "410","430", "450", "470", "490",
                        "510", "530", "550", "570", "590",
                        "610", "630", "650", "670", "690"
                    },
                    LabelsPaint = new SolidColorPaint(SKColors.Black),
                    LabelsDensity = 0,
                    IsVisible = true                           // 축 자체 보이도록
                }
            };

            YAxes = new Axis[]
            {
                    new Axis
                    {
                        Name = "Intensity",
                        NamePaint = new SolidColorPaint(SKColors.Black),
                        NameTextSize = 16,
                        NamePadding = new Padding(0, 0, 10, 0),

                        // 숫자 축으로 사용
                        MinLimit = 0,      // 최소 값
                        MaxLimit = 255,    // 최대 값
                        MinStep  = 51,     // 최소 간격 (0, 51, 102, ...)

                        // 값 → 문자열 포맷
                        Labeler = value => value.ToString("0"), // 필요하면 $"{value:0}" 같은 형식

                        LabelsPaint = new SolidColorPaint(SKColors.Black),
                        TextSize = 14,

                        LabelsDensity = 0,
                        IsVisible = true
                    }
            };
        }
        public void AddSpectrum(ChartModel chartModel, SpectrumData spectrumData)
        {
            _chartModel = chartModel;
            _spectrumDatas.Add(spectrumData);
        }
        public void RemoveSpectrum(SpectrumData spectrumData)
        {
            _spectrumDatas.Remove(spectrumData);
        }
        public void Clear()
        {
            _chartModel = null;
            _spectrumDatas.Clear();
        }
    }
}
