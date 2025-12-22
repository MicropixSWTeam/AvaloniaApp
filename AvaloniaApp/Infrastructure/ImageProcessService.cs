using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public partial class ImageProcessService
    {
        public FrameData CloneFrameData(FrameData src) => FrameData.CloneFullFrame(src);

        // [신규] 전처리 로직 (OpenCV In-place 처리)
        public unsafe void ProcessFrame(FrameData frame)
        {
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
            // 필요 시 여기에 정규화, 노이즈 제거 등의 로직 추가
            // Cv2.Normalize(mat, mat, 0, 255, NormTypes.MinMax);
        }

        public FrameData GetCropFrameData(FrameData fullframe, int previewIndex, int wd = 0)
        {
            var coordinates = Options.GetCoordinates(wd);
            var rect = coordinates[previewIndex];
            return CropFrameData(fullframe, rect);
        }

        public List<FrameData> GetCropFrameDatas(FrameData fullframe, int wd = 0)
        {
            var coordinates = Options.GetCoordinates(wd);
            var results = new List<FrameData>(coordinates.Count);
            try
            {
                for (int i = 0; i < coordinates.Count; i++)
                {
                    results.Add(CropFrameData(fullframe, coordinates[i]));
                }
                return results;
            }
            catch
            {
                foreach (var r in results) r.Dispose();
                results.Clear();
                throw;
            }
        }

        public FrameData GetRgbFrameData(FrameData fullframe, int wd = 0)
        {
            int idxBlue = Options.GetWavelengthIndexMap()[450];
            int idxGreen = Options.GetWavelengthIndexMap()[530];
            int idxRed = Options.GetWavelengthIndexMap()[630];

            var coords = Options.GetCoordinates(wd);
            var rectBlue = coords[idxBlue];
            var rectGreen = coords[idxGreen];
            var rectRed = coords[idxRed];

            // 1. 각 채널 Crop (최적화된 CloneCropFrame 사용)
            using var frameB = CropFrameData(fullframe, rectBlue);
            using var frameG = CropFrameData(fullframe, rectGreen);
            using var frameR = CropFrameData(fullframe, rectRed);

            int w = frameB.Width;
            int h = frameB.Height;
            int len = w * h * 3; // 3채널

            // 2. OpenCV Merge
            // 각 채널을 Mat으로 변환 (데이터 복사 없이 포인터만 사용)
            using var matB = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameB.Bytes, frameB.Stride);
            using var matG = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameG.Bytes, frameG.Stride);
            using var matR = Mat.FromPixelData(h, w, MatType.CV_8UC1, frameR.Bytes, frameR.Stride);

            // 결과 담을 버퍼
            var rgbBuffer = ArrayPool<byte>.Shared.Rent(len);

            try
            {
                // MatType.CV_8UC3 (Blue, Green, Red 순서 - OpenCV 기본)
                // 만약 Avalonia Bitmap이 BGRA 등을 요구한다면 Alpha 채널 추가 필요할 수 있음
                // 여기서는 일단 RGB(또는 BGR) 3채널 Raw Data를 만듦
                using var matDst = Mat.FromPixelData(h, w, MatType.CV_8UC3, rgbBuffer);

                // 채널 병합 (B, G, R 순서로 넣어야 BGR 포맷이 됨 -> Avalonia는 보통 BGRA나 RGB 지원)
                // 여기서는 PixelFormats.Rgb24 대응을 위해 R, G, B 순서로 Merge하거나
                // OpenCV 기본 BGR로 하고 PixelFormats.Bgr24를 쓸 수 있음. (아래 Convert에서 Bgr24 사용 예정)
                Cv2.Merge(new[] { matB, matG, matR }, matDst);

                // FrameData 생성 (Stride는 w * 3)
                return FrameData.Wrap(rgbBuffer, w, h, w * 3, len);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(rgbBuffer);
                throw;
            }
        }
        public FrameData? GetStitchFrameData(List<FrameData> frames, int wd = 0)
        {
            if (frames == null || frames.Count == 0) return null;

            int entireW = Options.EntireWidth;
            int entireH = Options.EntireHeight;
            int dstLen = entireW * entireH;
            var coordinates = Options.GetCoordinates(wd);
            var stitchBuffer = ArrayPool<byte>.Shared.Rent(dstLen);
            Array.Clear(stitchBuffer, 0, dstLen);

            try
            {
                int count = Math.Min(frames.Count, coordinates.Count);
                unsafe
                {
                    fixed (byte* pDstBase = stitchBuffer)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var crop = frames[i];
                            var rect = coordinates[i];
                            int x = Math.Max(0, (int)rect.X);
                            int y = Math.Max(0, (int)rect.Y);
                            int w = Math.Min(crop.Width, entireW - x);
                            int h = Math.Min(crop.Height, entireH - y);

                            if (w <= 0 || h <= 0) continue;

                            fixed (byte* pSrcBase = crop.Bytes)
                            {
                                for (int row = 0; row < h; row++)
                                {
                                    byte* pSrc = pSrcBase + (long)row * crop.Stride;
                                    byte* pDst = pDstBase + (long)(y + row) * entireW + x;
                                    Buffer.MemoryCopy(pSrc, pDst, w, w);
                                }
                            }
                        }
                    }
                }
                return FrameData.Wrap(stitchBuffer, entireW, entireH, entireW, dstLen);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(stitchBuffer);
                throw;
            }
        }

        public unsafe void ConvertFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = Math.Min(frame.Width, fb.Size.Width);
            int height = Math.Min(frame.Height, fb.Size.Height);

            int srcStride = frame.Stride;
            int dstStride = fb.RowBytes;
            int copyWidth = width;

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                if (dstStride == srcStride && width == frame.Width && dstStride == width)
                {
                    long totalBytes = (long)dstStride * height;
                    if (totalBytes <= frame.Length)
                    {
                        Buffer.MemoryCopy(src, dst, totalBytes, totalBytes);
                        return;
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        copyWidth,
                        copyWidth);
                }
            }
        }
        public unsafe void ConvertRgbFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = Math.Min(frame.Width, fb.Size.Width);
            int height = Math.Min(frame.Height, fb.Size.Height);

            int srcStride = frame.Stride;
            int dstStride = fb.RowBytes;
            // 3 bytes per pixel
            int copyWidthBytes = width * 3;

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        copyWidthBytes,
                        copyWidthBytes);
                }
            }
        }
        public WriteableBitmap CreateBitmapFromFrame(FrameData frame, PixelFormat? format = null)
        {
            var pxFormat = format ?? PixelFormats.Gray8; // 기본값 Mono8
            // RGB 데이터라면 호출자가 PixelFormats.Bgr24 등을 명시해야 함

            var bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                pxFormat,
                AlphaFormat.Opaque);

            // 기존 변환 메서드 재사용 (Lock & Copy)
            if (pxFormat == PixelFormats.Bgr24 || pxFormat == PixelFormats.Rgb24)
            {
                ConvertRgbFrameDataToWriteableBitmap(bitmap, frame);
            }
            else
            {
                ConvertFrameDataToWriteableBitmap(bitmap, frame);
            }

            return bitmap;
        }
        private unsafe FrameData CropFrameData(FrameData src, Rect roi)
        {
            return FrameData.CloneCropFrame(src, roi);
        }
    }
}