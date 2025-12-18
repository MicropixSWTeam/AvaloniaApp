using Avalonia;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaloniaApp.Infrastructure
{
    public class RegionAnalysisService
    {
        private readonly ObservableCollection<SelectRegionData> _regions = new();
        public ReadOnlyObservableCollection<SelectRegionData> Regions { get; }

        // 나중 분석 단계를 위한 데이터 저장소
        private readonly Dictionary<int, List<IntensityData>> _details = new();

        private readonly HashSet<int> _usedColorIndices = new();
        private const int MaxColors = 15;

        public event EventHandler? Updated;

        public RegionAnalysisService()
        {
            Regions = new ReadOnlyObservableCollection<SelectRegionData>(_regions);
        }

        // ✅ 표시만: Release 때마다 누적 생성
        public SelectRegionData AddRoi(Rect controlRect)
        {
            int colorIndex = AllocateColorIndex();
            int newIndex = _regions.Count > 0 ? _regions.Max(r => r.Index) + 1 : 0;

            var region = new SelectRegionData
            {
                Index = newIndex,
                ControlRect = controlRect,
                ColorIndex = colorIndex,
                // 분석 값은 일단 0/default 처리
                Mean = 0,
                StdDev = 0,
                Rect = default
            };

            _regions.Add(region);
            Updated?.Invoke(this, EventArgs.Empty);
            return region;
        }

        // (기존) 분석 결과 추가 (나중 단계에서 사용)
        public void AddResult(Rect imageRect, double mean, double stdDev, List<IntensityData>? details = null)
        {
            int colorIndex = AllocateColorIndex();
            int newIndex = _regions.Count > 0 ? _regions.Max(r => r.Index) + 1 : 0;

            var newRegion = new SelectRegionData
            {
                Index = newIndex,
                Rect = imageRect,
                Mean = mean,
                StdDev = stdDev,
                ColorIndex = colorIndex
            };

            _regions.Add(newRegion);
            if (details != null)
                _details[newIndex] = details;

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveResult(SelectRegionData region)
        {
            if (_regions.Remove(region))
            {
                _usedColorIndices.Remove(region.ColorIndex);
                _details.Remove(region.Index);
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Clear()
        {
            _regions.Clear();
            _details.Clear();
            _usedColorIndices.Clear();
            Updated?.Invoke(this, EventArgs.Empty);
        }

        public List<IntensityData>? GetDetails(int index) => _details.TryGetValue(index, out var d) ? d : null;

        private int AllocateColorIndex()
        {
            for (int i = 0; i < MaxColors; i++)
            {
                if (!_usedColorIndices.Contains(i))
                {
                    _usedColorIndices.Add(i);
                    return i;
                }
            }
            return MaxColors - 1;
        }
    }

    // IntensityData 클래스가 없으면 컴파일 에러가 날 수 있으므로 필요 시 빈 클래스라도 정의
    public class IntensityData { /* 내용 생략 */ }
}