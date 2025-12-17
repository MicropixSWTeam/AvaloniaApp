using LiveChartsCore.SkiaSharpView.Avalonia;
using OpenCvSharp;
using System;
using System.Collections.Generic;


namespace AvaloniaApp.Core.Models
{
    public class Workspace : IDisposable
    {
        public FrameData? EntireFrameData { get; set; }
        public List<FrameData> CropFrameDatas { get; set; } = new();
        public FrameData? StitchFrameData { get; set; }
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
