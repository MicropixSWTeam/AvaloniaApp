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
        private static readonly SKColor[] Colors =
        {
            SKColors.Red,
            SKColors.LimeGreen,
            SKColors.DeepSkyBlue,
            SKColors.Orange,
            SKColors.Magenta,
            SKColors.Cyan,
            SKColors.Gold,
            SKColors.MediumPurple
        };

        public static SKColor GetChartColor(int regionIndex)
        {
            if (regionIndex <= 0) return Colors[0];
            int idx = (regionIndex - 1) % Colors.Length;
            return Colors[idx];
        }
    }
}
