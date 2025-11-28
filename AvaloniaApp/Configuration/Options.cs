using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Configuration
{
    public sealed class CameraOptions
    {
        public double   ExposureMs { get; init; } = 0.0;
        public double   Gain { get; init; } = 0.0;
        public double   Gamma { get; init; } = 0.0;
        public string   PixelFormat { get; init; } = "Mono8";   
    }
}
