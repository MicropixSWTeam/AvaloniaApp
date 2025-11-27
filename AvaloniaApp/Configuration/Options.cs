using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Configuration
{
    public sealed class CameraOptions
    {
        public double   DefaultExposureMs { get; init; } = 0.0;
        public double   DefaultGain { get; init; } = 0.0;
        public double   DefaultGamma { get; init; } = 0.0;
    }
}
