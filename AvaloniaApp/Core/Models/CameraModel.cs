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
    /// <summary>
    /// 각 타일별 평행 이동(translation) 보정 값
    /// OffsetX: +면 오른쪽, -면 왼쪽
    /// OffsetY: +면 아래, -면 위
    /// </summary>
    public readonly record struct TileTransform(int OffsetX, int OffsetY);

    /// <summary>
    /// 거리(cm 등)별 타일 translation 테이블.
    /// key: 거리 (10, 20, 30, 40...)
    /// value: 타일 인덱스 -> (dx, dy)
    /// 타일 인덱스 7(가운데)은 데이터가 없으면 (0,0)으로 취급.
    /// </summary>
    public static class TranslationOffsets
    {
        public static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, TileTransform>> Table
            = new Dictionary<int, IReadOnlyDictionary<int, TileTransform>>
            {
                [10] = new Dictionary<int, TileTransform>
                {
                    [0] = new TileTransform(38, 30),
                    [1] = new TileTransform(15, 25),
                    [2] = new TileTransform(-7, 19),
                    [3] = new TileTransform(-34, 13),
                    [4] = new TileTransform(-56, 6),
                    [5] = new TileTransform(41, 13),
                    [6] = new TileTransform(19, 7),
                    // [7] 없음 → (0,0) 가정
                    [8] = new TileTransform(-25, -8),
                    [9] = new TileTransform(-48, -13),
                    [10] = new TileTransform(50, -7),
                    [11] = new TileTransform(26, -16),
                    [12] = new TileTransform(6, -20),
                    [13] = new TileTransform(-22, -29),
                    [14] = new TileTransform(-40, -36),
                },
                [20] = new Dictionary<int, TileTransform>
                {
                    [0] = new TileTransform(18, 23),
                    [1] = new TileTransform(6, 17),
                    [2] = new TileTransform(-6, 10),
                    [3] = new TileTransform(-22, 5),
                    [4] = new TileTransform(-35, -2),
                    [5] = new TileTransform(22, 14),
                    [6] = new TileTransform(10, 7),
                    // [7] 없음
                    [8] = new TileTransform(-14, -9),
                    [9] = new TileTransform(-26, -14),
                    [10] = new TileTransform(29, 4),
                    [11] = new TileTransform(17, -7),
                    [12] = new TileTransform(5, -12),
                    [13] = new TileTransform(-9, -20),
                    [14] = new TileTransform(-20, -27),
                },
                [30] = new Dictionary<int, TileTransform>
                {
                    [0] = new TileTransform(11, 19),
                    [1] = new TileTransform(2, 13),
                    [2] = new TileTransform(-7, 7),
                    [3] = new TileTransform(-19, 1),
                    [4] = new TileTransform(-27, -5),
                    [5] = new TileTransform(16, 13),
                    [6] = new TileTransform(6, 7),
                    // [7] 없음
                    [8] = new TileTransform(-11, -9),
                    [9] = new TileTransform(-19, -13),
                    [10] = new TileTransform(21, 6),
                    [11] = new TileTransform(13, -4),
                    [12] = new TileTransform(6, -8),
                    [13] = new TileTransform(-6, -17),
                    [14] = new TileTransform(-12, -24),
                },
                [40] = new Dictionary<int, TileTransform>
                {
                    [0] = new TileTransform(7, 17),
                    [1] = new TileTransform(0, 11),
                    [2] = new TileTransform(-6, 5),
                    [3] = new TileTransform(-16, -1),
                    [4] = new TileTransform(-22, -7),
                    [5] = new TileTransform(12, 14),
                    [6] = new TileTransform(4, 7),
                    // [7] 없음
                    [8] = new TileTransform(-8, -9),
                    [9] = new TileTransform(-14, -13),
                    [10] = new TileTransform(17, 8),
                    [11] = new TileTransform(11, -2),
                    [12] = new TileTransform(6, -6),
                    [13] = new TileTransform(-4, -14),
                    [14] = new TileTransform(-8, -22),
                },
            };
    }
    public readonly record struct CropGridConfig(int RowSize,int ColSize,int RowGap,int ColGap,int RowCount,int ColCount);
    public sealed class ImageMatchData
    {
        public int XOffset { get; init; }   
        public int YOffset { get; init; }
        public int Distance { get; init; }  
    }

}
