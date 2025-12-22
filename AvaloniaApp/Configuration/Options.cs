using Avalonia;
using Avalonia.Media;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Utils; // Util 사용을 위해 추가
using Material.Styles.Themes;
using OpenCvSharp;
using OpenCvSharp.Detail;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        public static double MaxExposureTime { get; } = 1000000;
        public static double MinGain { get; } = 0;
        public static double MaxGain { get; } = 48;
        public static double MinGamma { get; } = 0.3;
        public static double MaxGamma { get; } = 2.8;
        public static int EntireWidth { get; } = 5328;
        public static int EntireHeight { get; } = 3040;
        public static int GridWidthSize { get; } = 708;
        public static int GridHeightSize { get; } = 704;
        public static int GridWidthGap { get; } = 1064;  
        public static int GridHeightGap { get; } = 1012; 
        public static int CropRowCount { get; } = 3;
        public static int CropColumnCount { get; } = 5;
        public static int CropTotalCount { get; } = 15;
        public static int CropWidthSize { get; } = 548;
        public static int CropHeightSize { get; } = 544; 
        public static int MaxRegionCount { get; } = 6;
        public static int DefaultWavelengthIndex{get;} = 7;
        public static int DefaultWorkingDistance{get;} = 0;
        // 2. 고정 팔레트: 차트 시리즈 색상과 반드시 일치시켜야 함
        private static readonly IReadOnlyList<IBrush> _drawBrushes = new List<IBrush>
        {
            Brushes.Red,
            Brushes.Green,
            Brushes.Blue,
            Brushes.Yellow,
            Brushes.Orange,
            Brushes.Purple,
        };
        public static IReadOnlyList<IBrush> GetDrawBrushes() => _drawBrushes;
        public static IBrush GetBrushByIndex(int index)
        {
            if (index < 0 || index >= _drawBrushes.Count) return Brushes.White;
            return _drawBrushes[index];
        }

        private static readonly IReadOnlyDictionary<int, int> _wavelengthIndexMap = new Dictionary<int, int>
        {
            { 490, 0 },{ 470, 1 },{ 450, 2 },{ 430, 3 },{ 410, 4 },
            { 590, 5 },{ 570, 6 }, { 550, 7 },{ 530, 8 }, { 510, 9 },
            { 690, 10 },{ 670, 11 },{ 650, 12 }, { 630, 13 }, { 610, 14 }
        };
        public static IReadOnlyDictionary<int, int> GetWavelengthIndexMap() => _wavelengthIndexMap;
        public static IReadOnlyList<int> GetWavelengthList()
        {
            return _wavelengthIndexMap
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Key)
                .ToArray(); // 또는 .ToList()
        }
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
            new ComboBoxData{ DisplayText = "50cm", NumericValue = 50},
            new ComboBoxData{ DisplayText = "60cm", NumericValue = 60},
        };
        public static IReadOnlyList<ComboBoxData> GetWorkingDistanceComboBoxData() => _workingDistance;

        // 먼저 정의되어야 함 (아래 CoordinateTable 계산에 사용)
        private static readonly ImmutableDictionary<int, IReadOnlyList<Offset>> _workingDistanceOffsetMap = new Dictionary<int, IReadOnlyList<Offset>>
        {
            // [0] Working Distance 0 (기준점: 오프셋 없음)
            [0] = new Offset[]
            {
                new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0),
                new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0),
                new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0), new Offset(0,0)
            }.ToImmutableList(),

            // [10] Working Distance 10 (로그 블록 1)
            [10] = new Offset[]
            {
                new Offset(-42,-41), new Offset(-17,-33), new Offset(8,-24), new Offset(35,-15), new Offset(59,-5),
                new Offset(-50,-17), new Offset(-25,-8),  new Offset(0,0),   new Offset(24,10),  new Offset(50,18),
                new Offset(-58,6),   new Offset(-33,15),   new Offset(-8,23),  new Offset(17,32),  new Offset(42,39)
            }.ToImmutableList(),

            // [20] Working Distance 20 (로그 블록 2)
            [20] = new Offset[]
            {
                new Offset(-13,-28), new Offset(-2,-20),  new Offset(9,-10),  new Offset(19,-1),  new Offset(30,8),
                new Offset(-21,-16), new Offset(-10,-7),  new Offset(0,0),    new Offset(10,11),  new Offset(22,18),
                new Offset(-29,-7),  new Offset(-19,3),   new Offset(-8,9),   new Offset(3,19),   new Offset(14,28)
            }.ToImmutableList(),

            // [30] Working Distance 30 (로그 블록 3)
            [30] = new Offset[]
            {
                new Offset(-4,-23),  new Offset(2,-15),   new Offset(8,-7),   new Offset(15,2),   new Offset(21,11),
                new Offset(-12,-16), new Offset(-6,-8),   new Offset(0,0),    new Offset(6,10),   new Offset(14,18),
                new Offset(-20,-11), new Offset(-15,-1),  new Offset(-8,5),   new Offset(-1,14),  new Offset(5,23)
            }.ToImmutableList(),

            // [40] Working Distance 40 (로그 블록 4)
            [40] = new Offset[]
            {
                new Offset(0,-21),   new Offset(4,-13),   new Offset(8,-5),   new Offset(12,3),   new Offset(17,13),
                new Offset(-8,-16),  new Offset(-4,-8),   new Offset(0,0),    new Offset(4,9),    new Offset(9,17),
                new Offset(-16,-13), new Offset(-13,-4),  new Offset(-8,3),   new Offset(-3,12),  new Offset(1,20)
            }.ToImmutableList(),
        }.ToImmutableDictionary();

        public static IReadOnlyDictionary<int, IReadOnlyList<Offset>> GetWorkingDistanceOffsetMap() => _workingDistanceOffsetMap;
        public static IReadOnlyList<Offset> GetWorkingDistanceOffsets(int key) => _workingDistanceOffsetMap[key];

        private static readonly IReadOnlyList<Rect> _baseRects =
            Util.CreateCoordinates(
                EntireWidth, EntireHeight,
                CropWidthSize, CropHeightSize,
                GridWidthGap,  
                GridHeightGap,   
                CropColumnCount,
                CropRowCount
            );

        private static readonly ImmutableDictionary<int, IReadOnlyList<Rect>> _workingDistanceCoordinateTable =
            _workingDistanceOffsetMap.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<Rect>)Util.CalculateOffsetCropRects(
                    _baseRects,
                    kvp.Value,
                    EntireWidth,
                    EntireHeight
                )
            );

        public static IReadOnlyDictionary<int, IReadOnlyList<Rect>> GetWorkingDistanceCoordinateTable => _workingDistanceCoordinateTable;
        public static IReadOnlyList<Rect> GetCoordinates(int wd) => _workingDistanceCoordinateTable.ContainsKey(wd) ? _workingDistanceCoordinateTable[wd] : _workingDistanceCoordinateTable[0];
        #endregion
    }
}