using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Configuration
{
    public sealed class CameraOptions
    {
        public double ExposureMs { get; init; } = 100.0;
        public double Gain { get; init; } = 0.0;
        public double Gamma { get; init; } = 0.3;
    }

    public sealed class CameraConnectViewOptions
    {
        public string Title { get; init; } = "Camera Connect";
        public int Width { get; init; } = 640;
        public int Height { get; init; } = 480;
    }

    public sealed class AxisOptions
    {
        public string Label { get; init; } = "";
    }

    public sealed class SpectrumChartViewOptions
    {
        public string Title { get; init; } = "Spectrum Chart";

        public AxisOptions XAxis { get; init; } = new();
        public AxisOptions YAxis { get; init; } = new();
    }

    public sealed class ViewOptions
    {
        public CameraConnectViewOptions CameraConnect { get; init; } = new();
        public SpectrumChartViewOptions SpectrumChart { get; init; } = new();
    }

    public sealed class AppOptions
    {
        public CameraOptions Camera { get; init; } = new();
        public ViewOptions Views { get; init; } = new();
    }

}
