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

            if (ws.IntensityDataMap == null || ws.IntensityDataMap.Count == 0)
            {
                if (SeriesCollection.Count > 0)
                    _service.Ui.InvokeAsync(() => SeriesCollection.Clear());
                return;
            }

            _service.Ui.InvokeAsync(() =>
            {
                if (SeriesCollection.Count != ws.IntensityDataMap.Count)
                    RebuildAllSeries(ws.IntensityDataMap);
                else
                    UpdateExistingSeries(ws.IntensityDataMap);
            });
        }

        private void RebuildAllSeries(IReadOnlyDictionary<int, IntensityData[]> map)
        {
            SeriesCollection.Clear();
            foreach (var kvp in map.OrderBy(k => k.Key))
            {
                SeriesCollection.Add(CreateSeries(kvp.Key, kvp.Value));
            }
        }

        private void UpdateExistingSeries(IReadOnlyDictionary<int, IntensityData[]> map)
        {
            int i = 0;
            foreach (var kvp in map.OrderBy(k => k.Key))
            {
                if (i >= SeriesCollection.Count) break;
                SeriesCollection[i].Points = kvp.Value.Select(d => new ChartPoint(d.wavelength, d.mean, d.stddev)).ToList();
                i++;
            }
        }

        private ChartSeries CreateSeries(int regionIndex, IntensityData[] intensities)
        {
            return new ChartSeries
            {
                Id = regionIndex.ToString(),
                DisplayName = $"Region {regionIndex + 1}",
                Points = intensities.Select(d => new ChartPoint(d.wavelength, d.mean, d.stddev)).ToList(),
                LinePen = new Pen(Options.GetBrushByIndex(regionIndex), 1),
                MarkerFill = Options.GetBrushByIndex(regionIndex),
                ShowErrorBars = true
            };
        }

        public override async ValueTask DisposeAsync()
        {
            _workspaceService.Updated -= OnAnalysisCompleted;
            await base.DisposeAsync();
        }
    }
}