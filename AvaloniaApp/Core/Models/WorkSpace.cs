using AvaloniaApp.Configuration;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaloniaApp.Core.Models
{
    public class Workspace : IDisposable
    {
        private readonly ObservableCollection<RegionData> _regionDatas = new();
        private IReadOnlyDictionary<int, IntensityData[]> _intensityDataMap = new Dictionary<int, IntensityData[]>();

        public ReadOnlyObservableCollection<RegionData> RegionDatas => new(_regionDatas);
        public IReadOnlyDictionary<int, IntensityData[]> IntensityDataMap => _intensityDataMap;

        public FrameData? EntireFrameData { get; private set; }
        public FrameData? ColorFrameData { get; private set; }
        public int WorkingDistance { get; private set; } = 0;

        public int GetNextAvailableIndex()
        {
            if (_regionDatas.Count >= Options.MaxRegionCount) return -1;

            var usedIndices = _regionDatas.Select(r => r.Index).ToHashSet();
            for (int i = 0; i < Options.MaxRegionCount; i++)
            {
                if (!usedIndices.Contains(i)) return i;
            }
            return -1;
        }

        public void AddRegionData(Rect rect)
        {
            int targetIndex = GetNextAvailableIndex();
            if (targetIndex == -1) return;

            var region = new RegionData
            {
                Index = targetIndex,
                Rect = rect
            };

            _regionDatas.Add(region);
        }

        public void UpdateIntensityDataMap(IReadOnlyDictionary<int, IntensityData[]> map)
        {
            _intensityDataMap = map;
        }

        public void RemoveRegionData(RegionData region)
        {
            _regionDatas.Remove(region);
        }

        public void ClearRegionDatas() => _regionDatas.Clear();

        public void SetEntireFrameData(FrameData? frame)
        {
            EntireFrameData?.Dispose();
            EntireFrameData = frame;
        }

        public void SetColorFrameData(FrameData? frame)
        {
            ColorFrameData?.Dispose();
            ColorFrameData = frame;
        }

        public void SetWorkingDistance(int wd)
        {
            WorkingDistance = wd;
        }

        public void Dispose()
        {
            SetEntireFrameData(null);
            GC.SuppressFinalize(this);
        }
    }
}