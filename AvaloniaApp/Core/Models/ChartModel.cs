using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Models
{
    public static class RegionColorPalette
    {
        private static readonly (byte r, byte g, byte b)[] Palette =
        {
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
            (255, 165, 0),
            (255, 255, 0),
            (128, 0, 128),
        };
        public static SKColor GetSkColor(int index)
        {
            var (r, g, b) = Palette[index % Palette.Length];
            return new SKColor(r, g, b);
        }
        public static Color GetAvaloniaColor(int index)
        {
            var (r, g, b) = Palette[index % Palette.Length];
            return Color.FromRgb(r, g, b);
        }
    }
}
