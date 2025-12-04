// AvaloniaApp.Core/Models/CameraAndAnalysisModels.cs
using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvaloniaApp.Core.Models
{
    public sealed class CameraInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string SerialNumber { get; init; } = string.Empty;
        public string ModelName { get; init; } = string.Empty;

        public CameraInfo(string id, string name, string serial, string modelName)
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

    public readonly record struct CropGridConfig(
        int RowSize,
        int ColSize,
        int RowGap,
        int ColGap,
        int RowCount,
        int ColCount);

    /// <summary>
    /// 하나의 선택 영역(ROI)에 대한 정보.
    /// </summary>
    public sealed class SelectionRegion
    {
        public int Index { get; }
        public Rect ControlRect { get; }      // CameraView 캔버스 좌표
        public Rect ImageRect { get; }        // 타일 이미지 좌표
        public double Mean { get; }
        public double StdDev { get; }

        // Canvas 배치용 프로퍼티
        public double X => ControlRect.X;
        public double Y => ControlRect.Y;
        public double Width => ControlRect.Width;
        public double Height => ControlRect.Height;

        public SelectionRegion(
            int index,
            Rect controlRect,
            Rect imageRect,
            double mean,
            double stdDev)
        {
            Index = index;
            ControlRect = controlRect;
            ImageRect = imageRect;
            Mean = mean;
            StdDev = stdDev;
        }
    }

    /// <summary>
    /// 하나의 ROI에 대해, 각 타일별 mean / stdDev 배열 (차트용).
    /// </summary>
    public sealed class RegionSeries
    {
        public int RegionIndex { get; }
        public IReadOnlyList<double> Means { get; }
        public IReadOnlyList<double> StdDevs { get; }

        public RegionSeries(
            int regionIndex,
            IReadOnlyList<double> means,
            IReadOnlyList<double> stdDevs)
        {
            RegionIndex = regionIndex;
            Means = means;
            StdDevs = stdDevs;
        }
    }

    /// <summary>
    /// 타일 하나에 대한 Y(mean, stdDev) 통계.
    /// </summary>
    public sealed record TileStats(double Mean, double StdDev);

    /// <summary>
    /// 선택 영역(ROI) + 각 영역에 대한 타일별 통계를 공유하는 워크스페이스.
    /// CameraViewModel, ChartViewModel 등에서 DI로 공유.
    /// </summary>
    public sealed class RegionAnalysisWorkspace
    {
        private readonly ObservableCollection<SelectionRegion> _regions = new();
        public ObservableCollection<SelectionRegion> Regions => _regions;

        /// <summary>
        /// 선택된 영역(Chart에서 선택해서 Remove할 때 사용).
        /// </summary>
        public SelectionRegion? SelectedRegion { get; set; }

        /// <summary>
        /// Region.Index → 타일별 통계 리스트(예: 15개 TileStats).
        /// </summary>
        public Dictionary<int, IReadOnlyList<TileStats>> RegionTileStats { get; } = new();

        /// <summary>
        /// Regions/RegionTileStats가 변경될 때마다 발생.
        /// ChartViewModel에서 Subscribe해서 Series 재구성.
        /// </summary>
        public event EventHandler? Changed;

        public void AddRegion(SelectionRegion region, IReadOnlyList<TileStats> tileStats)
        {
            if (region is null) throw new ArgumentNullException(nameof(region));
            if (tileStats is null) throw new ArgumentNullException(nameof(tileStats));

            // 같은 Index가 있으면 먼저 제거
            var existing = _regions.FirstOrDefault(r => r.Index == region.Index);
            if (existing is not null)
            {
                _regions.Remove(existing);
                RegionTileStats.Remove(existing.Index);
            }

            _regions.Add(region);
            RegionTileStats[region.Index] = tileStats;
            OnChanged();
        }

        public void RemoveRegion(SelectionRegion region)
        {
            if (region is null) return;
            if (_regions.Remove(region))
            {
                RegionTileStats.Remove(region.Index);
                if (ReferenceEquals(SelectedRegion, region))
                    SelectedRegion = null;
                OnChanged();
            }
        }

        public void Clear()
        {
            _regions.Clear();
            RegionTileStats.Clear();
            SelectedRegion = null;
            OnChanged();
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 현재 프레임 + 타일(정규화된 타일 포함) + ROI 영역 + ROI별 시리즈를
    /// 공용으로 관리하는 Workspace.
    /// CameraViewModel, ChartViewModel 등이 DI로 공유해서 사용.
    /// </summary>
    public sealed class ImageAnalysisWorkspace : IDisposable
    {
        public WriteableBitmap? SourceGrayFrame { get; private set; }

        /// <summary>정규화 + translation까지 완료된 타일들(Gray8) </summary>
        public IReadOnlyList<WriteableBitmap> NormalizedTiles { get; private set; }
            = Array.Empty<WriteableBitmap>();

        /// <summary>그려진 ROI 사각형 목록 (CameraView에서 사용)</summary>
        public ObservableCollection<SelectionRegion> Regions { get; } = new();

        /// <summary>각 ROI에 대응하는 타일별 mean/stdDev (ChartViewModel에서 사용)</summary>
        public ObservableCollection<RegionSeries> RegionSeries { get; } = new();

        private int _nextRegionIndex = 1;
        public int NextRegionIndex => _nextRegionIndex;

        public ImageAnalysisWorkspace()
        {
        }

        public void SetFrameAndTiles(WriteableBitmap? frame, IReadOnlyList<WriteableBitmap> tiles)
        {
            // 기존 리소스 해제
            SourceGrayFrame?.Dispose();
            if (NormalizedTiles is { Count: > 0 })
            {
                foreach (var t in NormalizedTiles)
                    t.Dispose();
            }

            SourceGrayFrame = frame;
            NormalizedTiles = tiles ?? Array.Empty<WriteableBitmap>();

            // 새 프레임이 들어오면 ROI/시리즈는 초기화 (새 측정으로 간주)
            Regions.Clear();
            RegionSeries.Clear();
            _nextRegionIndex = 1;
        }

        public int AllocateRegionIndex()
        {
            return _nextRegionIndex++;
        }

        public void AddRegion(SelectionRegion region, RegionSeries series)
        {
            Regions.Add(region);
            RegionSeries.Add(series);
        }

        public void ClearRegions()
        {
            Regions.Clear();
            RegionSeries.Clear();
            _nextRegionIndex = 1;
        }

        public void RemoveLastRegion()
        {
            if (Regions.Count == 0 || RegionSeries.Count == 0)
                return;

            Regions.RemoveAt(Regions.Count - 1);
            RegionSeries.RemoveAt(RegionSeries.Count - 1);
        }

        public void Dispose()
        {
            SourceGrayFrame?.Dispose();
            if (NormalizedTiles is { Count: > 0 })
            {
                foreach (var t in NormalizedTiles)
                    t.Dispose();
            }
        }
    }
}
