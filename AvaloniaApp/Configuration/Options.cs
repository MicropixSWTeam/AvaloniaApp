using Avalonia;
using Avalonia.Media;
using AvaloniaApp.Core.Models;
using Material.Styles.Themes;
using OpenCvSharp;
using OpenCvSharp.Detail;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Configuration
{
    public static class Options
    {
        #region Default 값들 나중에 json화 예정
        public static double MinExposureTime { get; } = 100;
        public static double MaxExposureTime { get;  } = 1000000;
        public static double MinGain { get; } = 0;
        public static double MaxGain { get; } = 48;
        public static double MinGamma { get; } = 0.3;   
        public static double MaxGamma { get;  } = 2.8;
        public static int EntireWidth { get; } = 5328;
        public static int EntireHeight { get;  } = 3040;
        public static int CropRowCount { get; } = 3;
        public static int CropColumnCount { get; } = 5;
        public static int CropTotalCount { get; } = 15;
        public static int CropWidth { get;} = 548;
        public static int CropHeight { get; } = 548;

        public static int MaxRegionCount { get; } = 6;

        // 2. 고정 팔레트: 차트 시리즈 색상과 반드시 일치시켜야 함
        private static readonly IReadOnlyList<IBrush> _drawBrushes = new List<IBrush>
        {
            Brushes.Red,         // Index 0
            Brushes.Lime,        // Index 1
            Brushes.DodgerBlue,  // Index 2
            Brushes.Orange,      // Index 3
            Brushes.Yellow,      // Index 4
            Brushes.Magenta      // Index 5
        };
        public static IReadOnlyList<IBrush> GetDrawBrushes() => _drawBrushes;
        public static IBrush GetBrushByIndex(int index)
        {
            if (index < 0 || index >= _drawBrushes.Count) return Brushes.White;
            return _drawBrushes[index];
        }
        // Mapping 된 Index 값    
        private static readonly int[] _mappedIndex = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        public static int ConvertIndexToMappedIndex(int index) => (uint)index < (uint)_mappedIndex.Length ? _mappedIndex[index] : -1;

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
        public static Rect[] GetAllCoordinates() => _coordinates.ToArray();
        public static Rect GetCoordinateByIndex(int index)
        {
            if ((uint)index >= (uint)_coordinates.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _coordinates[index];
        }
        private static readonly IReadOnlyDictionary<int, int> _wavelengthIndexMap = new Dictionary<int, int>
        {
            { 410, 0 }, { 430, 1 }, { 450, 2 }, { 470, 3 }, { 490, 4 },
            { 510, 5 }, { 530, 6 }, { 550, 7 }, { 570, 8 }, { 590, 9 },
            { 610, 10 }, { 630, 11 }, { 650, 12 }, { 670, 13 }, { 690, 14 }
        };
        public static IReadOnlyDictionary<int, int> GetWavelengthIndexMap() => _wavelengthIndexMap;
        
        private static readonly ComboBoxData[] _wavelengthIndexComboBoxData 
            = _wavelengthIndexMap.
            OrderBy(kvp => kvp.Key).
            Select(kvp => new ComboBoxData
            {
                DisplayText = $"{kvp.Key}nm",
                NumericValue = kvp.Value
            }).ToArray();
        public static IReadOnlyList<ComboBoxData> GetWavelengthIndexComboBoxData() => _wavelengthIndexComboBoxData;

        private static readonly ComboBoxData[] _workingDistance = new ComboBoxData[]
        {
            new ComboBoxData{ DisplayText = "Over", NumericValue = 0},
            new ComboBoxData{ DisplayText = "10cm", NumericValue = 10},
            new ComboBoxData{ DisplayText = "20cm", NumericValue = 20},
            new ComboBoxData{ DisplayText = "30cm", NumericValue = 30},
            new ComboBoxData{ DisplayText = "40cm", NumericValue = 40},
        };

        public static IReadOnlyList<ComboBoxData> GetWorkingDistanceComboBoxData() => _workingDistance;
        #endregion
    }
}
