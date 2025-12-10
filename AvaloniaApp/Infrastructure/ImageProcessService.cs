// AvaloniaApp.Infrastructure/ImageProcessService.cs
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using MathNet.Numerics.IntegralTransforms;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Rect = Avalonia.Rect;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// - 카메라 프레임 해상도 + crop 그리드 설정으로 타일 좌표 생성/캐시
    /// - 전체 이미지를 grid 기준으로 먼저 crop (타일)
    /// - 각 타일 내부에 translation 적용 (dx, dy)
    /// - 각 타일을 targetIntensity(평균 밝기)에 맞게 normalize
    /// - normalize된 타일들을 다시 붙여서 stitching (translation 적용 가능)
    /// - 위상 상관(Phase Correlation) 또는 템플릿 매칭으로 타일 간 offset 추정
    /// </summary>
    public class ImageProcessService
    {
        private readonly List<Rect> _tileRects = new();
        private bool _gridConfigured;
        private PixelSize _frameSize;
        private CropGridConfig _config;

        // translation 프리뷰 좌표 캐시: distance -> [tileIndex] -> 사전 계산된 정보
        private readonly Dictionary<int, TranslationPreviewCoord[]> _translationPreviewTable = new();
        private readonly List<TranslationPreviewCoord> _translationPreviewCoordArray = new();

        private readonly struct TranslationPreviewCoord
        {
            public TranslationPreviewCoord(PixelRect sourceRect, int destOffsetX, int destOffsetY)
            {
                SourceRect = sourceRect;
                DestOffsetX = destOffsetX;
                DestOffsetY = destOffsetY;
            }

            public PixelRect SourceRect { get; }
            public int DestOffsetX { get; }
            public int DestOffsetY { get; }
        }

        public IReadOnlyList<Rect> TileRects => _tileRects;
        public int TileCount => _tileRects.Count;
        public PixelSize FrameSize => _frameSize;
        public CropGridConfig GridConfig => _config;

        public ImageProcessService()
        {
        }
        
        #region Grid 설정

        public void ConfigureGrid(
            int cameraWidth,
            int cameraHeight,
            int rowSize,
            int colSize,
            int rowGap,
            int colGap,
            int rowCount,
            int colCount)
        {
            ConfigureGrid(
                new PixelSize(cameraWidth, cameraHeight),
                new CropGridConfig(rowSize, colSize, rowGap, colGap, rowCount, colCount));
        }

        /// <summary>
        /// frameSize + CropGridConfig 로 grid 좌표 생성
        /// </summary>
        public void ConfigureGrid(PixelSize frameSize, CropGridConfig config)
        {
            if (frameSize.Width <= 0 || frameSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (config.RowSize <= 0 || config.ColSize <= 0)
                throw new ArgumentOutOfRangeException("RowSize/ColSize");
            if (config.RowCount <= 0 || config.ColCount <= 0)
                throw new ArgumentOutOfRangeException("RowCount/ColCount");

            _frameSize = frameSize;
            _config = config;

            _tileRects.Clear();

            int cameraWidth = frameSize.Width;
            int cameraHeight = frameSize.Height;

            int cx = cameraWidth / 2;
            int cy = cameraHeight / 2;

            int rowSize = config.RowSize;
            int colSize = config.ColSize;
            int rowGap = config.RowGap;
            int colGap = config.ColGap;
            int rowCount = config.RowCount;
            int colCount = config.ColCount;

            // 중앙 기준 grid 좌표 생성
            for (int i = 0; i < rowCount; ++i)
            {
                double iOff = i - (rowCount - 1) * 0.5;
                for (int j = 0; j < colCount; ++j)
                {
                    double jOff = j - (colCount - 1) * 0.5;

                    int x1 = (int)Math.Round(
                        cx - (colSize * 0.5) + jOff * colGap);

                    int y1 = (int)Math.Round(
                        cy - (rowSize * 0.5) + iOff * rowGap);

                    _tileRects.Add(new Rect(x1, y1, colSize, rowSize));
                }
            }

            // distance / index 별 translation 프리뷰 좌표 미리 계산
            BuildTranslationPreviewTable();

            _gridConfigured = true;
        }
        /// <summary>
        /// 현재 Grid(ROW x COL) 설정을 기준으로,
        /// 기본 인덱스(위에서 아래, 왼→오른쪽) 순서로 타일 인덱스를 반환.
        /// 예) 3x5 → 0 1 2 3 4 / 5 6 7 8 9 / 10 11 12 13 14
        /// </summary>
        public IEnumerable<int> GetTileIndicesTopToBottom()
        {
            if (!_gridConfigured)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다.");

            int rowCount = _config.RowCount;
            int colCount = _config.ColCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < colCount; col++)
                {
                    yield return row * colCount + col;
                }
            }
        }
        /// <summary>
        /// 현재 Grid(ROW x COL) 설정을 기준으로,
        /// "아래 행 → 위 행" 순서로 타일 인덱스를 반환.
        /// 예) 3x5:
        ///   반환 순서: 10 11 12 13 14, 5 6 7 8 9, 0 1 2 3 4
        /// </summary>
        public IEnumerable<int> GetTileIndicesBottomToTop()
        {
            if (!_gridConfigured)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다.");

            int rowCount = _config.RowCount;
            int colCount = _config.ColCount;

            for (int row = rowCount - 1; row >= 0; row--)
            {
                for (int col = 0; col < colCount; col++)
                {
                    yield return row * colCount + col;
                }
            }
        }

        /// <summary>
        /// "위→아래, 왼→오른쪽" 기준 인덱스를
        /// 세로로 플립한 인덱스로 변환.
        /// </summary>
        public int FlipVerticalIndex(int index)
        {
            if (!_gridConfigured)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다.");

            int colCount = _config.ColCount;
            int rowCount = _config.RowCount;

            int total = rowCount * colCount;
            if (index < 0 || index >= total)
                throw new ArgumentOutOfRangeException(nameof(index));

            int row = index / colCount;
            int col = index % colCount;

            int flippedRow = rowCount - 1 - row;
            return flippedRow * colCount + col;
        }

        #endregion

        #region Crop / Translation / Normalize

        /// <summary>
        /// translation 없이 crop:
        /// 전체 프레임에서 grid 좌표에 맞게 타일 하나 잘라냄.
        /// (1단계: 전체 이미지를 crop)
        /// </summary>
        public WriteableBitmap CropTile(Bitmap source, int tileIndex)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다. ConfigureGrid를 먼저 호출하세요.");
            if (tileIndex < 0 || tileIndex >= _tileRects.Count)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));
            if (source.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={source.Format}");

            var baseRect = _tileRects[tileIndex];
            var roiRect = ToPixelRectClamped(baseRect, _frameSize);
            if (roiRect.Width <= 0 || roiRect.Height <= 0)
                throw new InvalidOperationException("ROI가 프레임 내부에 없습니다.");

            var roiSize = new PixelSize(roiRect.Width, roiRect.Height);

            var cropped = new WriteableBitmap(
                roiSize,
                new Avalonia.Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            using (var fb = cropped.Lock())
            {
                int bufferSize = fb.RowBytes * fb.Size.Height;
                source.CopyPixels(roiRect, fb.Address, bufferSize, fb.RowBytes);
            }

            return cropped;
        }

        /// <summary>
        /// 전체 이미지를 grid로 crop한 후, 해당 타일 내부에서 translation 적용.
        /// (2단계: crop된 타일에서 dx,dy 만큼 평행 이동)
        /// </summary>
        public WriteableBitmap CropTile(Bitmap source, int tileIndex, TileTransform transform)
        {
            var baseTile = CropTile(source, tileIndex); // 1단계: 전체 이미지에서 crop

            if (transform.OffsetX == 0 && transform.OffsetY == 0)
            {
                // 이동 없음 → 그대로 사용
                return baseTile;
            }

            // 2단계: crop된 타일 내부에서 평행이동
            var translated = ApplyTranslation(baseTile, transform);
            baseTile.Dispose();
            return translated;
        }

        /// <summary>
        /// 실시간 프리뷰용: (전체 → crop → translation → normalize)
        /// </summary>
        public WriteableBitmap NormalizeTile(Bitmap source, int tileIndex, TileTransform transform, byte targetIntensity)
        {
            var tile = CropTile(source, tileIndex, transform); // crop + translation
            NormalizeInPlace(tile, targetIntensity);           // normalize
            return tile;
        }

        /// <summary>
        /// translation/normalize 없이 grid 전체 타일 normalize (분석용).
        /// (전체 → crop → normalize)
        /// </summary>
        public IReadOnlyList<Bitmap> NormalizeTiles(Bitmap source, byte targetIntensity)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다. ConfigureGrid를 먼저 호출하세요.");
            if (source.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={source.Format}");

            var result = new Bitmap[_tileRects.Count];

            for (int i = 0; i < _tileRects.Count; i++)
            {
                var tile = CropTile(source, i);           // crop
                NormalizeInPlace(tile, targetIntensity); // normalize
                result[i] = tile;
            }

            return result;
        }

        #endregion

        #region Stitching

        /// <summary>
        /// translation 없이 stitching.
        /// (이미 타일 내부 translation/normalize를 끝낸 상태라고 가정)
        /// </summary>
        public Bitmap StitchTiles(IReadOnlyList<Bitmap> tiles)
        {
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다.");
            if (tiles is null) throw new ArgumentNullException(nameof(tiles));
            if (tiles.Count != _tileRects.Count)
                throw new ArgumentException("tiles 개수와 grid 타일 수가 일치하지 않습니다.", nameof(tiles));
            if (_frameSize.Width <= 0 || _frameSize.Height <= 0)
                throw new InvalidOperationException("유효하지 않은 frameSize입니다.");

            if (tiles.Count == 0)
                throw new InvalidOperationException("타일이 없습니다.");

            var final = new WriteableBitmap(
                _frameSize,
                new Avalonia.Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            using (var fbFinal = final.Lock())
            {
                int finalStride = fbFinal.RowBytes;

                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    var roiRect = ToPixelRectClamped(_tileRects[i], _frameSize);
                    BlitTile(tile, fbFinal.Address, finalStride, roiRect);
                }
            }

            return final;
        }

        /// <summary>
        /// translation 적용 stitching.
        /// (타일 내부 translation까지 적용한 상태에서, mosaic 상의 위치를 추가로 보정하고 싶을 때 사용)
        /// </summary>
        public Bitmap StitchTiles(IReadOnlyList<Bitmap> tiles, IReadOnlyList<TileTransform> transforms)
        {
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다.");
            if (tiles is null) throw new ArgumentNullException(nameof(tiles));
            if (transforms is null) throw new ArgumentNullException(nameof(transforms));
            if (tiles.Count != _tileRects.Count || transforms.Count != _tileRects.Count)
                throw new ArgumentException("tiles / transforms 개수가 grid 타일 수와 일치하지 않습니다.");
            if (_frameSize.Width <= 0 || _frameSize.Height <= 0)
                throw new InvalidOperationException("유효하지 않은 frameSize입니다.");

            if (tiles.Count == 0)
                throw new InvalidOperationException("타일이 없습니다.");

            var final = new WriteableBitmap(
                _frameSize,
                new Avalonia.Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            using (var fbFinal = final.Lock())
            {
                int finalStride = fbFinal.RowBytes;

                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    var baseRect = _tileRects[i];
                    var t = transforms[i];

                    var shifted = OffsetRect(baseRect, t.OffsetX, t.OffsetY);
                    var roiRect = ToPixelRectClamped(shifted, _frameSize);

                    BlitTile(tile, fbFinal.Address, finalStride, roiRect);
                }
            }

            return final;
        }

        #endregion

        #region 내부 구현: Translation / Normalize / Blit

        /// <summary>
        /// crop된 타일 내부에서 dx, dy 만큼 평행 이동.
        /// dx &gt; 0 → 오른쪽, dx &lt; 0 → 왼쪽
        /// dy &gt; 0 → 아래, dy &lt; 0 → 위
        /// 이동 범위를 벗어나는 픽셀은 버리고, 새로 생기는 영역은 0(검정)으로 채움.
        /// </summary>
        private static WriteableBitmap ApplyTranslation(WriteableBitmap sourceTile, TileTransform transform)
        {
            if (sourceTile.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={sourceTile.Format}");

            using (var fbSrc = sourceTile.Lock())
            {
                int width = fbSrc.Size.Width;
                int height = fbSrc.Size.Height;
                int srcStride = fbSrc.RowBytes;
                int srcSize = srcStride * height;

                if (width <= 0 || height <= 0 || srcSize <= 0)
                    return sourceTile;

                byte[] srcBuffer = ArrayPool<byte>.Shared.Rent(srcSize);

                try
                {
                    Marshal.Copy(fbSrc.Address, srcBuffer, 0, srcSize);

                    var dest = new WriteableBitmap(
                        fbSrc.Size,
                        new Avalonia.Vector(96, 96),
                        PixelFormats.Gray8,
                        AlphaFormat.Opaque);

                    using (var fbDst = dest.Lock())
                    {
                        int dstStride = fbDst.RowBytes;
                        int dstSize = dstStride * height;
                        byte[] dstBuffer = ArrayPool<byte>.Shared.Rent(dstSize);

                        try
                        {
                            // 0으로 초기화 (검정)
                            Array.Clear(dstBuffer, 0, dstSize);

                            int dx = transform.OffsetX;
                            int dy = transform.OffsetY;

                            // src (x,y) → dst (x+dx, y+dy)
                            for (int y = 0; y < height; y++)
                            {
                                int srcRow = y * srcStride;

                                int dstY = y + dy;
                                if (dstY < 0 || dstY >= height)
                                    continue;

                                int dstRow = dstY * dstStride;

                                for (int x = 0; x < width; x++)
                                {
                                    int dstX = x + dx;
                                    if (dstX < 0 || dstX >= width)
                                        continue;

                                    byte v = srcBuffer[srcRow + x];
                                    dstBuffer[dstRow + dstX] = v;
                                }
                            }

                            Marshal.Copy(dstBuffer, 0, fbDst.Address, dstSize);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(dstBuffer);
                        }
                    }

                    return dest;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(srcBuffer);
                }
            }
        }

        private static void BlitTile(Bitmap tile, IntPtr finalBase, int finalStride, PixelRect roiRect)
        {
            var tileSize = tile.PixelSize;
            var srcRect = new PixelRect(
                0,
                0,
                Math.Min(tileSize.Width, roiRect.Width),
                Math.Min(tileSize.Height, roiRect.Height));

            if (srcRect.Width <= 0 || srcRect.Height <= 0)
                return;

            IntPtr destBase = IntPtr.Add(
                finalBase,
                roiRect.Y * finalStride + roiRect.X); // Gray8: 1byte per pixel

            tile.CopyPixels(
                srcRect,
                destBase,
                finalStride * srcRect.Height,
                finalStride);
        }

        /// <summary>
        /// Mono8 타일 내부를 targetIntensity(평균 밝기)에 맞게 scale (0~255 클램프).
        /// </summary>
        private static void NormalizeInPlace(WriteableBitmap tile, byte targetIntensity)
        {
            using (var fb = tile.Lock())
            {
                int width = fb.Size.Width;
                int height = fb.Size.Height;
                int stride = fb.RowBytes;
                int bufferSize = stride * height;

                if (width <= 0 || height <= 0 || bufferSize <= 0)
                    return;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                try
                {
                    Marshal.Copy(fb.Address, buffer, 0, bufferSize);

                    long sum = 0;
                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            sum += buffer[rowStart + x];
                        }
                    }

                    int pixelCount = width * height;
                    if (pixelCount == 0)
                        return;

                    double mean = (double)sum / pixelCount;
                    if (mean <= 0.0)
                        return;

                    double scale = targetIntensity / mean;

                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowStart + x;
                            int v = (int)Math.Round(buffer[idx] * scale);
                            if (v < 0) v = 0;
                            else if (v > 255) v = 255;
                            buffer[idx] = (byte)v;
                        }
                    }

                    Marshal.Copy(buffer, 0, fb.Address, bufferSize);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        private static Rect OffsetRect(Rect rect, int dx, int dy)
        {
            return new Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
        }

        private static PixelRect ToPixelRectClamped(Rect rect, PixelSize frameSize)
        {
            int x = (int)Math.Round(rect.X);
            int y = (int)Math.Round(rect.Y);
            int w = (int)Math.Round(rect.Width);
            int h = (int)Math.Round(rect.Height);

            if (x < 0)
            {
                w += x;
                x = 0;
            }
            if (y < 0)
            {
                h += y;
                y = 0;
            }

            if (x + w > frameSize.Width)
                w = frameSize.Width - x;
            if (y + h > frameSize.Height)
                h = frameSize.Height - y;

            if (w < 0) w = 0;
            if (h < 0) h = 0;

            return new PixelRect(x, y, w, h);
        }

        #endregion

        #region Translation preview precompute

        /// <summary>
        /// 현재 Grid 설정(_tileRects, _frameSize, _config)을 유지한 채
        /// distance / tileIndex 별 translation 프리뷰 캐시를 다시 만든다.
        /// - TranslationOffsets.SetRuntimeOffsets(...) 호출 후에 부르면
        ///   런타임 보정 결과가 프리뷰에 반영된다.
        /// </summary>
        public void RebuildTranslationPreviewTable()
        {
            if (!_gridConfigured)
                return;

            BuildTranslationPreviewTable();
        }

        private void BuildTranslationPreviewTable()
        {
            _translationPreviewTable.Clear();

            if (_tileRects.Count == 0)
                return;

            int previewWidth = _config.ColSize;
            int previewHeight = _config.RowSize;

            // distance = 0 (기본 no-translation)
            var arr0 = new TranslationPreviewCoord[_tileRects.Count];
            for (int i = 0; i < _tileRects.Count; i++)
            {
                var baseRect = _tileRects[i];
                var roiPixel = ToPixelRectClamped(baseRect, _frameSize);

                if (roiPixel.Width <= 0 || roiPixel.Height <= 0)
                {
                    arr0[i] = new TranslationPreviewCoord(
                        new PixelRect(0, 0, 0, 0),
                        0,
                        0);
                    continue;
                }

                int offsetX = (previewWidth - roiPixel.Width) / 2;
                int offsetY = (previewHeight - roiPixel.Height) / 2;

                if (offsetX < 0) offsetX = 0;
                if (offsetY < 0) offsetY = 0;

                arr0[i] = new TranslationPreviewCoord(roiPixel, offsetX, offsetY);
            }

            _translationPreviewTable[0] = arr0;

            // 거리별 translation 적용된 ROI
            foreach (var kv in TranslationOffsets.Table)
            {
                int distance = kv.Key;

                var arr = new TranslationPreviewCoord[_tileRects.Count];

                for (int i = 0; i < _tileRects.Count; i++)
                {
                    var baseRect = _tileRects[i];

                    // 정적 Table + 런타임 보정 결과를 모두 포함해서 transform 을 가져온다.
                    var t = GetTransformFromDistance(distance, i);

                    var overlap = GetTranslationOverlapRect(baseRect, t);
                    if (overlap.Width <= 0 || overlap.Height <= 0)
                        overlap = baseRect;

                    var roiPixel = ToPixelRectClamped(overlap, _frameSize);

                    if (roiPixel.Width <= 0 || roiPixel.Height <= 0)
                    {
                        arr[i] = new TranslationPreviewCoord(
                            new PixelRect(0, 0, 0, 0),
                            0,
                            0);
                        continue;
                    }

                    int offsetX = (previewWidth - roiPixel.Width) / 2;
                    int offsetY = (previewHeight - roiPixel.Height) / 2;

                    if (offsetX < 0) offsetX = 0;
                    if (offsetY < 0) offsetY = 0;

                    arr[i] = new TranslationPreviewCoord(roiPixel, offsetX, offsetY);
                }

                _translationPreviewTable[distance] = arr;
            }
        }

        private static TileTransform GetTransformFromDistance(int distance, int tileIndex)
        {
            // 정적 테이블 + 런타임 보정 테이블을 모두 고려해서 가져온다.
            return TranslationOffsets.GetTransformOrDefault(distance, tileIndex);
        }

        private static Rect IntersectRects(Rect a, Rect b)
        {
            double x1 = Math.Max(a.X, b.X);
            double y1 = Math.Max(a.Y, b.Y);
            double x2 = Math.Min(a.Right, b.Right);
            double y2 = Math.Min(a.Bottom, b.Bottom);

            double w = x2 - x1;
            double h = y2 - y1;

            if (w <= 0 || h <= 0)
                return new Rect(0, 0, 0, 0);

            return new Rect(x1, y1, w, h);
        }

        private static Rect GetTranslationOverlapRect(Rect baseRect, TileTransform transform)
        {
            if (transform.OffsetX == 0 && transform.OffsetY == 0)
                return baseRect;

            var shifted = OffsetRect(baseRect, transform.OffsetX, transform.OffsetY);
            return IntersectRects(baseRect, shifted);
        }

        /// <summary>
        /// distance + tileIndex 로 미리 계산된 translation ROI 를 사용해서,
        /// 전체 이미지에서 해당 영역만 crop 하고
        /// RowSize x ColSize 크기의 Gray8 Bitmap 중앙에 배치해서 반환 (프리뷰용).
        /// </summary>
        public WriteableBitmap GetTranslationCropImage(Bitmap source, int distance, int tileIndex)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다. ConfigureGrid를 먼저 호출하세요.");
            if (tileIndex < 0 || tileIndex >= _tileRects.Count)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));
            if (source.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={source.Format}");

            if (!_translationPreviewTable.TryGetValue(distance, out var coords) || coords is null)
                throw new ArgumentOutOfRangeException(nameof(distance), "지원하지 않는 distance 입니다.");

            var coord = coords[tileIndex];

            if (coord.SourceRect.Width <= 0 || coord.SourceRect.Height <= 0)
                throw new InvalidOperationException("사전 계산된 ROI가 유효하지 않습니다.");

            int previewWidth = _config.ColSize;
            int previewHeight = _config.RowSize;
            var previewSize = new PixelSize(previewWidth, previewHeight);

            var preview = new WriteableBitmap(
                previewSize,
                new Avalonia.Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            using (var fb = preview.Lock())
            {
                int stride = fb.RowBytes;
                int bufferSize = stride * previewHeight;

                byte[] clearBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    Array.Clear(clearBuffer, 0, bufferSize);
                    Marshal.Copy(clearBuffer, 0, fb.Address, bufferSize);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(clearBuffer);
                }

                IntPtr destBase = IntPtr.Add(
                    fb.Address,
                    coord.DestOffsetY * stride + coord.DestOffsetX);

                source.CopyPixels(
                    coord.SourceRect,
                    destBase,
                    stride * coord.SourceRect.Height,
                    stride);
            }

            return preview;
        }

        #endregion

        #region 타일 사전 구축 (Normalize + Translation 1회 수행)

        /// <summary>
        /// 현재 grid 설정과 transforms, targetIntensity를 기반으로
        /// 전체 타일(0..TileCount-1)에 대해
        ///  - CropTile(source, i, transform(i))
        ///  - NormalizeInPlace(tile, targetIntensity)
        /// 를 한 번만 수행해서 WriteableBitmap 리스트로 반환.
        /// 이후 ROI 분석에서는 이 리스트만 사용.
        /// </summary>
        public IReadOnlyList<WriteableBitmap> BuildNormalizedTiles(
            Bitmap source,
            Func<int, TileTransform> transformSelector,
            byte targetIntensity)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (!_gridConfigured || _tileRects.Count == 0)
                throw new InvalidOperationException("Grid가 아직 설정되지 않았습니다. ConfigureGrid를 먼저 호출하세요.");
            if (source.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={source.Format}");

            var tiles = new WriteableBitmap[_tileRects.Count];

            for (int i = 0; i < _tileRects.Count; i++)
            {
                var t = transformSelector?.Invoke(i) ?? new TileTransform(0, 0);

                var tile = CropTile(source, i, t);       // crop + translation
                NormalizeInPlace(tile, targetIntensity); // in-place normalize
                tiles[i] = tile;
            }

            return tiles;
        }

        /// <summary>
        /// 이미 translation 이 적용된 타일(WriteableBitmap)에 대해
        /// 평균 밝기를 targetIntensity 로 맞춘다.
        /// </summary>
        public void NormalizeTileInPlace(WriteableBitmap tile, byte targetIntensity)
        {
            if (tile is null) throw new ArgumentNullException(nameof(tile));
            if (tile.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={tile.Format}");

            NormalizeInPlace(tile, targetIntensity);
        }

        /// <summary>
        /// 이미 translation 이 적용된 타일을 normalize 하고,
        /// 동일한 인스턴스를 반환한다(체이닝용).
        /// </summary>
        public WriteableBitmap NormalizeTile(WriteableBitmap tile, byte targetIntensity)
        {
            NormalizeTileInPlace(tile, targetIntensity);
            return tile;
        }

        #endregion

        #region Template Matching 기반 오프셋 계산

        /// <summary>
        /// 기준 타일(referenceTile)의 referenceRectInReferenceTile 영역을 템플릿으로 사용해서,
        /// targetTile 에서 가장 잘 맞는 위치를 찾고,
        /// 그 차이를 TileTransform(dx, dy) 로 반환한다.
        /// dx, dy 는 "target 타일을 얼마나 평행 이동시키면 기준 타일의 ref 위치에 맞을지"를 의미.
        /// </summary>
        public TileTransform ComputeTemplateMatchOffset(
            WriteableBitmap referenceTile,
            Rect referenceRectInReferenceTile,
            WriteableBitmap targetTile)
        {
            if (referenceTile is null) throw new ArgumentNullException(nameof(referenceTile));
            if (targetTile is null) throw new ArgumentNullException(nameof(targetTile));

            if (referenceTile.Format != PixelFormats.Gray8 ||
                targetTile.Format != PixelFormats.Gray8)
            {
                throw new NotSupportedException("현재는 Mono8(Gray8) 타일만 지원합니다.");
            }

            var refSize = referenceTile.PixelSize;
            var tgtSize = targetTile.PixelSize;

            if (refSize.Width != tgtSize.Width || refSize.Height != tgtSize.Height)
                throw new InvalidOperationException("referenceTile과 targetTile의 해상도는 동일해야 합니다.");

            // 기준 Rect를 타일 내부로 클램프 후 정수 PixelRect로 변환
            var refRectPx = ToPixelRectClamped(referenceRectInReferenceTile, refSize);
            if (refRectPx.Width <= 0 || refRectPx.Height <= 0)
                return new TileTransform(0, 0);

            // WriteableBitmap → Mat (Gray8)
            using var refMat = GrayTileToMat(referenceTile);
            using var tgtMat = GrayTileToMat(targetTile);

            var tplRect = new OpenCvSharp.Rect(
                refRectPx.X,
                refRectPx.Y,
                refRectPx.Width,
                refRectPx.Height);

            // 템플릿 크기 방어
            if (tplRect.Width <= 0 || tplRect.Height <= 0 ||
                tplRect.X < 0 || tplRect.Y < 0 ||
                tplRect.Right > refMat.Cols || tplRect.Bottom > refMat.Rows)
            {
                return new TileTransform(0, 0);
            }

            using var template = new Mat(refMat, tplRect);

            int resultCols = tgtMat.Cols - template.Cols + 1;
            int resultRows = tgtMat.Rows - template.Rows + 1;
            if (resultCols <= 0 || resultRows <= 0)
                return new TileTransform(0, 0);

            using var result = new Mat(resultRows, resultCols, MatType.CV_32FC1);

            // 템플릿 매칭 (정규화 상관계수 방식)
            Cv2.MatchTemplate(tgtMat, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out double _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

            // 기준/매칭 위치의 중심 좌표
            double refCx = tplRect.X + tplRect.Width / 2.0;
            double refCy = tplRect.Y + tplRect.Height / 2.0;

            double matchCx = maxLoc.X + tplRect.Width / 2.0;
            double matchCy = maxLoc.Y + tplRect.Height / 2.0;

            // target 타일을 dx,dy 만큼 평행이동시키면 match 중심이 ref 중심으로 오도록 계산
            int dx = (int)Math.Round(refCx - matchCx);
            int dy = (int)Math.Round(refCy - matchCy);

            return new TileTransform(dx, dy);
        }

        /// <summary>
        /// Gray8 WriteableBitmap 을 동일 크기의 CV_8UC1 Mat 으로 변환.
        /// </summary>
        private static Mat GrayTileToMat(WriteableBitmap tile)
        {
            if (tile.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={tile.Format}");

            var size = tile.PixelSize;
            int width = size.Width;
            int height = size.Height;

            var mat = new Mat(height, width, MatType.CV_8UC1);

            using (var fb = tile.Lock())
            {
                int strideSrc = fb.RowBytes;
                int bufferSize = strideSrc * height;
                if (bufferSize <= 0)
                    return mat;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    Marshal.Copy(fb.Address, buffer, 0, bufferSize);

                    IntPtr dstBase = mat.Data;
                    int strideDst = width; // CV_8UC1 기본 step = width

                    for (int y = 0; y < height; y++)
                    {
                        IntPtr dstRow = IntPtr.Add(dstBase, y * strideDst);
                        Marshal.Copy(buffer, y * strideSrc, dstRow, width);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return mat;
        }

        #endregion

        #region 위상 상관 기반 오프셋 계산

        /// <summary>
        /// MathNet.Numerics 의 Managed Provider 는 2D FFT(Forward2D/Inverse2D)를
        /// 지원하지 않으므로, 1D FFT(행 → 열 순서)를 이용해서 2D FFT를 직접 구현.
        /// data 는 행 우선(row-major) [y * width + x] 레이아웃을 따른다.
        /// </summary>
        private static void Forward2DManaged(Complex[] data, int height, int width, FourierOptions options)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length != height * width)
                throw new ArgumentException("data length != height*width");

            // 1) 행 방향 FFT
            var row = new Complex[width];
            for (int y = 0; y < height; y++)
            {
                int offset = y * width;
                Array.Copy(data, offset, row, 0, width);
                Fourier.Forward(row, options);
                Array.Copy(row, 0, data, offset, width);
            }

            // 2) 열 방향 FFT
            var col = new Complex[height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    col[y] = data[y * width + x];
                }

                Fourier.Forward(col, options);

                for (int y = 0; y < height; y++)
                {
                    data[y * width + x] = col[y];
                }
            }
        }

        private static void Inverse2DManaged(Complex[] data, int height, int width, FourierOptions options)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length != height * width)
                throw new ArgumentException("data length != height*width");

            // 1) 행 방향 iFFT
            var row = new Complex[width];
            for (int y = 0; y < height; y++)
            {
                int offset = y * width;
                Array.Copy(data, offset, row, 0, width);
                Fourier.Inverse(row, options);
                Array.Copy(row, 0, data, offset, width);
            }

            // 2) 열 방향 iFFT
            var col = new Complex[height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    col[y] = data[y * width + x];
                }

                Fourier.Inverse(col, options);

                for (int y = 0; y < height; y++)
                {
                    data[y * width + x] = col[y];
                }
            }
        }

        /// <summary>
        /// 위상 상관(Phase Correlation)을 사용하여 기준 타일(referenceIndex)을 기준으로
        /// 나머지 타일들의 평행 이동 오프셋(정수 픽셀)을 계산.
        /// </summary>
        public IReadOnlyList<TileTransform> ComputePhaseCorrelationOffsets(
            IReadOnlyList<WriteableBitmap> tiles,
            int referenceIndex)
        {
            if (tiles is null) throw new ArgumentNullException(nameof(tiles));
            if (tiles.Count == 0) throw new ArgumentException("타일이 없습니다.", nameof(tiles));
            if (referenceIndex < 0 || referenceIndex >= tiles.Count)
                throw new ArgumentOutOfRangeException(nameof(referenceIndex));

            var refTile = tiles[referenceIndex];

            if (refTile.Format != PixelFormats.Gray8)
                throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={refTile.Format}");

            int width = refTile.PixelSize.Width;
            int height = refTile.PixelSize.Height;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("타일 크기가 유효하지 않습니다.");

            var options = FourierOptions.Default;

            // 기준 타일 FFT 준비
            var refFreq = new Complex[width * height];
            {
                using var fb = refTile.Lock();
                int stride = fb.RowBytes;
                int bufferSize = stride * height;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                try
                {
                    Marshal.Copy(fb.Address, buffer, 0, bufferSize);

                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            byte v = buffer[rowStart + x];
                            refFreq[y * width + x] = new Complex(v, 0);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                // 2D FFT
                Forward2DManaged(refFreq, height, width, options);
            }

            var results = new TileTransform[tiles.Count];

            for (int i = 0; i < tiles.Count; i++)
            {
                if (i == referenceIndex)
                {
                    results[i] = new TileTransform(0, 0);
                    continue;
                }

                var tile = tiles[i];

                if (tile.Format != PixelFormats.Gray8)
                    throw new NotSupportedException($"현재는 Mono8(Gray8)만 지원합니다. Format={tile.Format}");

                if (tile.PixelSize.Width != width || tile.PixelSize.Height != height)
                    throw new InvalidOperationException("모든 타일의 해상도는 동일해야 합니다.");

                var tileFreq = new Complex[width * height];

                // 대상 타일 FFT
                using (var fb = tile.Lock())
                {
                    int stride = fb.RowBytes;
                    int bufferSize = stride * height;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                    try
                    {
                        Marshal.Copy(fb.Address, buffer, 0, bufferSize);

                        for (int y = 0; y < height; y++)
                        {
                            int rowStart = y * stride;
                            for (int x = 0; x < width; x++)
                            {
                                byte v = buffer[rowStart + x];
                                tileFreq[y * width + x] = new Complex(v, 0);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                Forward2DManaged(tileFreq, height, width, options);

                // 교차 파워 스펙트럼 (정규화)
                var crossPower = new Complex[width * height];
                for (int k = 0; k < crossPower.Length; k++)
                {
                    Complex F = refFreq[k];
                    Complex G = tileFreq[k];
                    Complex R = F * Complex.Conjugate(G);
                    double mag = R.Magnitude;

                    crossPower[k] = mag < 1e-12 ? Complex.Zero : R / mag;
                }

                // 역 FFT → 위상 상관 맵
                Inverse2DManaged(crossPower, height, width, options);

                // 최대값 위치 찾기
                int peakIndex = 0;
                double maxVal = double.MinValue;

                for (int k = 0; k < crossPower.Length; k++)
                {
                    double val = crossPower[k].Magnitude;
                    if (val > maxVal)
                    {
                        maxVal = val;
                        peakIndex = k;
                    }
                }

                int peakY = peakIndex / width;
                int peakX = peakIndex % width;

                // 주기 경계 보정
                if (peakX > width / 2)
                    peakX -= width;
                if (peakY > height / 2)
                    peakY -= height;

                results[i] = new TileTransform(peakX, peakY);
            }

            return results;
        }

        #endregion
    }
}
