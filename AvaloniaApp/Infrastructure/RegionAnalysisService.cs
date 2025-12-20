using Avalonia;
using Avalonia.Media;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaloniaApp.Infrastructure
{
    public class RegionAnalysisService
    {
        private readonly ObservableCollection<RegionData> _regions = new();
        public ReadOnlyObservableCollection<RegionData> Regions { get; }

        public RegionAnalysisService()
        {
            Regions = new ReadOnlyObservableCollection<RegionData>(_regions);
        }

        /// <summary>
        /// 현재 사용 가능한 가장 작은 색상 인덱스를 찾습니다. 6개가 꽉 찼으면 -1을 반환합니다.
        /// </summary>
        public int GetNextAvailableColorIndex()
        {
            if (_regions.Count >= Options.MaxRegionCount) return -1;

            var usedIndices = _regions.Select(r => r.Index).ToHashSet();
            for (int i = 0; i < Options.MaxRegionCount; i++)
            {
                if (!usedIndices.Contains(i)) return i;
            }
            return -1;
        }

        public bool AddRegion(Rect controlRect)
        {
            int targetIndex = GetNextAvailableColorIndex();
            if (targetIndex == -1) return false;

            var region = new RegionData
            {
                Index = targetIndex,
                Rect = controlRect,
                Mean = 0,
                StdDev = 0
            };

            _regions.Add(region);
            return true;
        }

        public void RemoveRegion(RegionData region)
        {
            if (_regions.Remove(region))
            {
            }
        }

        public void ClearAll()
        {
            _regions.Clear();
        }
    }
}