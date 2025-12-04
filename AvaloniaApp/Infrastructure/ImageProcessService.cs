using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// - 카메라 프레임 해상도 + crop 그리드 설정으로 타일 좌표 생성/캐시
    /// - 전체 이미지를 grid 기준으로 먼저 crop (타일)
    /// - 각 타일 내부에 translation 적용 (dx, dy)
    /// - 각 타일을 targetIntensity(평균 밝기)에 맞게 normalize
    /// - normalize된 타일들을 다시 붙여서 stitching (translation 적용 가능)
    /// </summary>
    public class ImageProcessService
    {
        private readonly List<Rect> _tileRects = new();
        private bool _gridConfigured;
        private PixelSize _frameSize;
        private CropGridConfig _config;

        public IReadOnlyList<Rect> TileRects => _tileRects;
        public int TileCount => _tileRects.Count;
        public PixelSize FrameSize => _frameSize;
        public CropGridConfig GridConfig => _config;

        public ImageProcessService()
        {
        }

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

            // C++ 코드와 동일한 방식으로 grid 좌표 생성
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

            _gridConfigured = true;
        }

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
                new Vector(96, 96),
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
                var tile = CropTile(source, i);         // crop
                NormalizeInPlace(tile, targetIntensity); // normalize
                result[i] = tile;
            }

            return result;
        }

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
                new Vector(96, 96),
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
                new Vector(96, 96),
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

        // ===== 내부 구현부 =====

        /// <summary>
        /// crop된 타일 내부에서 dx, dy 만큼 평행 이동.
        /// dx &gt; 0 → 오른쪽으로 이동, dx &lt; 0 → 왼쪽
        /// dy &gt; 0 → 아래로 이동, dy &lt; 0 → 위로 이동
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
                        new Vector(96, 96),
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
    }
}
