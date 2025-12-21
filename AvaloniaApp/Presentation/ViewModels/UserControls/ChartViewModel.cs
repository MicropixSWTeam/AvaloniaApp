// ===== AvaloniaApp.Presentation/ViewModels/UserControls/ChartViewModel.cs (체크박스 + 색상 연동 버전) =====
using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class ChartViewModel : ViewModelBase
    {
        [ObservableProperty] private ObservableCollection<ChartSeries> _seriesCollection = new();

        private readonly WorkspaceService _workspaceService;

        public ChartViewModel(AppService service) : base(service)
        {
            _workspaceService = service.WorkSpace;
            _workspaceService.Updated -= OnAnalysisCompleted;
            _workspaceService.Updated += OnAnalysisCompleted;
        }

        private void OnAnalysisCompleted()
        {
            var ws = _service.WorkSpace.Current;
            if (ws == null) return;
            if (ws.IntensityDataMap == null || ws.IntensityDataMap.Count == 0)
                return;
            _service.Ui.InvokeAsync(() =>
            {
                SeriesCollection.Clear();
                foreach (var kvp in ws.IntensityDataMap)
                {
                    var regionIndex = kvp.Key;
                    var intensities = kvp.Value;

                    var series = new ChartSeries
                    {
                        DisplayName = $"Region {regionIndex + 1}",
                        Points = intensities.Select(d => new ChartPoint(d.wavelength, d.mean, d.stddev)).ToList(),
                        LinePen = new Pen(Options.GetBrushByIndex(regionIndex), 1),
                        MarkerFill = Options.GetBrushByIndex(regionIndex),
                        ShowErrorBars = true
                    };
                    SeriesCollection.Add(series);
                }
            });
        }
        public override async ValueTask DisposeAsync()
        {
            // 이벤트 구독 해제 (필수)
            _workspaceService.Updated -= OnAnalysisCompleted;
            await base.DisposeAsync();
        }
    }
}