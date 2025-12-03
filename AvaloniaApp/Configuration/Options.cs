using Avalonia;
using OpenCvSharp.Detail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Configuration
{
    public sealed class Options
    {
        public Options() { }
        public double MinExposureTime { get; set; } = 100;
        public double MaxExposureTime { get; set; } = 1000000;
        public double MinGain { get; set; } = 0;
        public double MaxGain { get; set; } = 48;
        public double MinGamma { get; set; } = 0.3;   
        public double MaxGamma { get; set; } = 2.8;  
        public List<Rect> Coordinates { get; set; } = new();
        public List<int> WaveLengths { get; set; } = new();
        public List<Point> MatchDatas { get; set; } = new();
    }
}
