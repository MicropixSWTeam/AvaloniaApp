using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.ViewModels.Base;
using AvaloniaApp.Presentation.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class ChartViewModel : ViewModelBase
    {
        [ObservableProperty] private ObservableCollection<ChartSeries> _seriesCollection = new();

        private readonly WorkspaceService _workspaceService;
        private readonly VimbaCameraService _cameraService;

        public ChartViewModel(AppService service) : base(service)
        {
            _workspaceService = service.WorkSpace;
            _cameraService = service.Camera;

            _workspaceService.Updated -= OnAnalysisCompleted;
            _workspaceService.Updated += OnAnalysisCompleted;
        }

        private void OnAnalysisCompleted()
        {
            var ws = _service.WorkSpace.Current;
            if (ws == null) return;

            // null이면 빈 딕셔너리로 취급 (삭제 시 대비)
            var map = ws.IntensityDataMap ?? new Dictionary<int, IntensityData[]>();

            _service.Ui.InvokeAsync(() => SyncSeries(map));
        }

        // [최종 로직] ID 기반 Sync & TryParse 안전장치
        private void SyncSeries(IReadOnlyDictionary<int, IntensityData[]> map)
        {
            // 1. [삭제] 맵에 없는 ID를 가진 시리즈 제거
            var itemsToRemove = new List<ChartSeries>();
            foreach (var series in SeriesCollection)
            {
                // ID가 "0", "1" 같은 정수 형태가 아니거나, 맵에 키가 없으면 삭제 대상
                if (!int.TryParse(series.Id, out int id) || !map.ContainsKey(id))
                {
                    itemsToRemove.Add(series);
                }
            }
            foreach (var item in itemsToRemove)
            {
                SeriesCollection.Remove(item);
            }

            // 2. [추가 및 업데이트]
            foreach (var kvp in map)
            {
                int index = kvp.Key;
                var dataList = kvp.Value;
                string seriesId = index.ToString();

                var chartPoints = dataList
                    .Select(d => new ChartPoint(d.wavelength, d.mean, d.stddev))
                    .ToArray();

                var existingSeries = SeriesCollection.FirstOrDefault(s => s.Id == seriesId);

                if (existingSeries == null)
                {
                    // 신규 추가
                    var newSeries = new ChartSeries
                    {
                        Id = seriesId,
                        DisplayName = $"Region {index + 1}",
                        Points = chartPoints,
                        LinePen = new Pen(Options.GetBrushByIndex(index), 1),
                        MarkerFill = Options.GetBrushByIndex(index),
                        MarkerRadius = 2.5,
                        ShowErrorBars = true
                    };
                    SeriesCollection.Add(newSeries);
                }
                else
                {
                    // 기존 데이터 업데이트
                    existingSeries.Points = chartPoints;
                }
            }
        }

        public override async ValueTask DisposeAsync()
        {

            _workspaceService.Updated -= OnAnalysisCompleted;
            await base.DisposeAsync();
        }
    }
}