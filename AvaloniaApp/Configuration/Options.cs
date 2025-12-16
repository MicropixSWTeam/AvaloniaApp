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
        #region Default 값들 나중에 json화 예정
        public double MinExposureTime { get; set; } = 100;
        public double MaxExposureTime { get; set; } = 1000000;
        public double MinGain { get; set; } = 0;
        public double MaxGain { get; set; } = 48;
        public double MinGamma { get; set; } = 0.3;   
        public double MaxGamma { get; set; } = 2.8;
        public int EntireWidth { get; private set; } = 5328;
        public int EntireHeight { get; private set; } = 3040;
        public int CropRowCount { get; private set; } = 3;
        public int CropColumnCount { get; private set; } = 5;
        public int CropTotalCount { get; private set; } = 15;
        public int CropWidth { get; private set; } = 548;
        public int CropHeight { get; private set; } = 548;

        // Mapping 된 Index 값    
        private static readonly int[] _mappedIndex = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        public int ConvertIndexToMappedIndex(int index) => (uint)index < (uint)_mappedIndex.Length ? _mappedIndex[index] : -1;

        // Coordinate Default 값 
        private static readonly Rect[] _coordinates = new Rect[]
        {
            // Row 0 (y = 242)
            new Rect( 278,  242, 548, 548),
            new Rect(1334,  242, 548, 548),
            new Rect(2390,  242, 548, 548),
            new Rect(3446,  242, 548, 548),
            new Rect(4502,  242, 548, 548),

            // Row 1 (y = 1246)
            new Rect( 278, 1246, 548, 548),
            new Rect(1334, 1246, 548, 548),
            new Rect(2390, 1246, 548, 548),
            new Rect(3446, 1246, 548, 548),
            new Rect(4502, 1246, 548, 548),

            // Row 2 (y = 2250)
            new Rect( 278, 2250, 548, 548),
            new Rect(1334, 2250, 548, 548),
            new Rect(2390, 2250, 548, 548),
            new Rect(3446, 2250, 548, 548),
            new Rect(4502, 2250, 548, 548),
        };
        public Rect[] GetAllCoordinates() => _coordinates;
        public Rect GetCoordinateByIndex(int index)
        {
            if ((uint)index >= (uint)_coordinates.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _coordinates[index];
        }
        #endregion
    }
}
