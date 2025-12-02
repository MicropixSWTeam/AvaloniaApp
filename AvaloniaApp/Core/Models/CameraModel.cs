using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Models
{
    public sealed class CameraInfo
    {
        public string   Id { get; init; } = string.Empty;
        public string   Name { get; init; } = string.Empty;
        public string   SerialNumber { get; init; } = string.Empty;
        public string   ModelName { get; init; } = string.Empty;

        public CameraInfo(string id, string name, string serial,string modelName)
        {
            Id = id;
            Name = name;
            SerialNumber = serial;
            ModelName = modelName;
        }
        public override string ToString()
        {
            return $"{Name}";
        }
    }
    public sealed class PixelFormatInfo
    {
        public string Name { get; }
        public string DisplayName { get; }
        public bool IsAvailable { get; }
        public PixelFormatInfo(string name, string displayName, bool isAvailable)
        {
            Name = name;
            DisplayName = displayName;
            IsAvailable = isAvailable;
        }

        public override string ToString()
            => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    }
}
