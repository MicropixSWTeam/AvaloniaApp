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
        public FrameData? EntireFrameData { get; private set; }
        public List<FrameData> CropFrameDatas { get; private set; } = new();
        public FrameData? StitchFrameData { get; private set; }
        public ReadOnlyObservableCollection<RegionData> RegionDatas { get; }
        public int WorkingDistance { get; private set; } = 0;
        public Workspace()
        {
            RegionDatas = new ReadOnlyObservableCollection<RegionData>(_regionDatas);
        }
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
        public bool AddRegionData(Rect rect)
        {
            int targetIndex = GetNextAvailableIndex();
            if (targetIndex == -1) return false;

            var region = new RegionData
            {
                Index = targetIndex,
                Rect = rect,
                Mean = 0,
                StdDev = 0
            };

            _regionDatas.Add(region);
            return true;
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
        public void SetCropFrameDatas(IEnumerable<FrameData> frames)
        {
            ClearCropFrames();
            CropFrameDatas.AddRange(frames);
        }
        public void SetStitchFrameData(FrameData? frame)
        {
            StitchFrameData?.Dispose();
            StitchFrameData = frame;
        }
        public void ClearCropFrames()
        {
            foreach (var frame in CropFrameDatas)
                frame.Dispose();
            
            CropFrameDatas.Clear();
        }
        public void Dispose()
        {
            SetEntireFrameData(null);
            ClearCropFrames();
            SetStitchFrameData(null);
            GC.SuppressFinalize(this);
        }
    }
}
