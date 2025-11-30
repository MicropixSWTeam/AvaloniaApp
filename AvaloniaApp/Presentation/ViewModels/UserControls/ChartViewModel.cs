using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
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

    public partial class ChartViewModel:ViewModelBase
    {
        [ObservableProperty]
        private ISeries[]? series;

        [ObservableProperty]
        private Axis[]? xAxes;

        [ObservableProperty]
        private Axis[]? yAxes;

        public ChartViewModel()
        {
            // 예시 데이터: 평균 + 표준편차
            // x = 0,1,2,3 / mean, sd
            var means = new[] { 3d, 5d, 7d, 4d };
            var sds = new[] { 0.5, 0.8, 1.0, 0.6 };

            var errorValues = means
                .Select((m, i) => new ErrorValue(m, sds[i])) // m ± sd
                .ToArray();

            Series = new ISeries[]
            {
                new LineSeries<ErrorValue>
                {
                    Name   = "Value ± SD",
                    Values = errorValues,
                    Fill = null,
                    // 라인/포인트 스타일
                    Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
                    
                    GeometrySize = 10,
                    // 에러 바 스타일 (선 굵기 등)
                    ErrorPaint = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 1 },
                }
            };
            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "X Axis",
                    LabelsRotation = 15,
                    TextSize = 14,
                    Labels = new[] { "0", "51", "102", "153","204","255" }   
                }
            };
            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Y Axis",
                    TextSize = 14
                }
            };
        }
    }
}
