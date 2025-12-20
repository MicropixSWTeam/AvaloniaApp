using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public class ImageProcessService
    {
        /// <summary>
        /// [필수] 전체 프레임 복제 (Workspace 보관용)
        /// Model.cs의 CloneFullFrame과 동일한 역할을 하지만 Service 계층에서 명시적으로 관리
        /// </summary>
        public FrameData CloneFrameData(FrameData src)
        {
            // Model.cs에 최적화된 CloneFullFrame이 있으므로 그것을 활용 (DRY 원칙)
            return FrameData.CloneFullFrame(src);
        }

        /// <summary>
        /// 단일 인덱스에 대한 Crop된 이미지 획득 (스트리밍 미리보기용)
        /// </summary>
        public FrameData GetCropFrameData(FrameData fullframe, int previewIndex,int wd = 0)
        {
            var rect = Options.GetCoordinates(wd)[previewIndex];

            // 1. Crop 및 버퍼 Rent
            var cropFrameData = CropFrameData(fullframe, rect);

            try
            {
                // 2. 이미지 처리 (DarkFrame, Normalize 등)
                BaseProcess(cropFrameData);
                return cropFrameData;
            }
            catch
            {
                cropFrameData.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 전체 좌표에 대한 Crop 이미지 리스트 획득 (Workspace 갱신용)
        /// </summary>
        public List<FrameData> GetCropFrameDatas(FrameData fullframe,int wd = 0)
        {
            var coordinates = Options.GetCoordinates(wd);
            var results = new List<FrameData>(coordinates.Count);

            try
            {
                for (int i = 0; i < coordinates.Count; i++)
                {
                    var rect = coordinates[i];
                    var crop = CropFrameData(fullframe, rect);

                    try
                    {
                        BaseProcess(crop);
                        results.Add(crop);
                    }
                    catch
                    {
                        crop.Dispose();
                        throw;
                    }
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

        public FrameData? GetStitchFrameData(List<FrameData> frames,int wd = 0)
        {
            if (frames == null || frames.Count == 0) return null;

            int entireW = Options.EntireWidth;
            int entireH = Options.EntireHeight;
            int dstLen = entireW * entireH;

            var coordinates = Options.GetCoordinates(wd);
            var stitchBuffer = ArrayPool<byte>.Shared.Rent(dstLen);
            Array.Clear(stitchBuffer, 0, dstLen); // 배경 초기화

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

        /// <summary>
        /// WriteableBitmap으로 데이터 복사 (UI 렌더링용)
        /// </summary>
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

                if (dstStride == srcStride && width == frame.Width)
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

        private unsafe void BaseProcess(FrameData frame)
        {
            // OpenCV 처리가 필요하면 여기서 수행 (In-place)
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
            // 예: Cv2.Normalize(mat, mat, ...);
        }

        private unsafe FrameData CropFrameData(FrameData src, Rect roi)
        {
            // Model.cs의 CloneCropFrame이 이미 최적화되어 있으므로 활용
            return FrameData.CloneCropFrame(src, roi);
        }
    }
}