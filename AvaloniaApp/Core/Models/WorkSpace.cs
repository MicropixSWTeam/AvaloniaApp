using OpenCvSharp;
using System;
using System.Collections.Generic;


namespace AvaloniaApp.Core.Models
{
    public class WorkSpace : IDisposable
    {
        public FrameData? EntireFrameData { get; set; }
        public List<FrameData> CropFrameDatas { get; set; } = new();
        public FrameData? StitchFrameData { get; set; }
        public List<RegionData> RegionsDatas { get; set; } = new();
        public void Dispose()
        {
            EntireFrameData?.Dispose();
            foreach (var cropFrameData in CropFrameDatas)
            {
                cropFrameData.Dispose();
            }
            StitchFrameData?.Dispose();
            foreach (var regionData in RegionsDatas)
            {
                regionData.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
