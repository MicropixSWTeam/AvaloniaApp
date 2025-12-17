using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;
using LiveChartsCore.Kernel;
using AutoMapper.Configuration.Annotations;

namespace AvaloniaApp.Infrastructure
{
    public class ImageProcessServiceTest
    {
        private Options _options;
        public ImageProcessServiceTest(Options options)
        {
            _options = options;
        }
        /// <summary>
        /// 스트리밍중에 Crop된 이미지 획득
        /// </summary>
        public FrameData GetCropFrameData(FrameData fullframe,int previewIndex)
        {
            var rect = _options.GetCoordinateByIndex(previewIndex);

            var cropFrameData = CropFrameData(fullframe, rect); // translation이 적용예정
            try
            {
                BaseProcess(cropFrameData);// Darkframe , Normalize 진행
                return cropFrameData;
            }
            catch 
            {
                cropFrameData.Dispose();
                throw;
            }
        }
        /// <summary>
        /// Stop시에 전체이미지를 잘라서 Crop된 15개 이미지 획득
        /// </summary>
        public List<FrameData> GetCropFrameDatas(FrameData fullframe)
        {
            var coordinates = _options.GetAllCoordinates();  
            var results = new List<FrameData>(coordinates.Length);

            try
            {
                for (int i = 0; i < coordinates.Length; i++)
                {
                    var rect = coordinates[i];

                    var crop = CropFrameData(fullframe,rect); // translation이 적용예정
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
                throw;
            }
        }
        public FrameData GetStitchFrameData(List<FrameData> frames)
        {
            int entireW = _options.EntireWidth;
            int entireH = _options.EntireHeight;
            int dstLen = entireW * entireH;

            var stitchBuffer = ArrayPool<byte>.Shared.Rent(dstLen);
            Array.Clear(stitchBuffer, 0, dstLen);

            try
            {
                var coords = _options.GetAllCoordinates();
                int count = Math.Min(frames.Count, coords.Length);

                unsafe
                {
                    fixed (byte* pDstBase = stitchBuffer)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var crop = frames[i];
                            var rect = coords[i];

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
        public unsafe FrameData CropFrameData(FrameData src, Rect roi, Offset offset)
        {
            return null;
        }
        public unsafe void ConvertFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = frame.Width;
            int height = frame.Height;

            int srcStride = frame.Stride;     // packed라면 width
            int dstStride = fb.RowBytes;
            int rowBytes = width;             // Gray8

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                if (dstStride == srcStride)
                {
                    // 최적화된 블록 복사 (packed to packed)
                    Buffer.MemoryCopy(src, dst, (long)dstStride * height, frame.Length);
                    return;
                }

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        rowBytes,
                        rowBytes);
                }
            }
        }
        private unsafe void BaseProcess(FrameData frame)
        {
            fixed(byte*p = frame.Bytes)
            {
                using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Bytes, frame.Stride);
            }
        }
        private unsafe FrameData CropFrameData(FrameData src, Rect roi)
        {
            // ROI 범위 안전 장치
            int x = Math.Max(0, roi.X);
            int y = Math.Max(0, roi.Y);
            int w = Math.Min(roi.Width, src.Width - x);
            int h = Math.Min(roi.Height, src.Height - y);

            if (w <= 0 || h <= 0)
                throw new ArgumentException("Invalid ROI dimensions");

            int dstStride = w; // Packed row (Gray8)
            int dstLen = w * h;

            // [핵심 개선] ArrayPool에서 메모리 빌리기
            var dstBuffer = ArrayPool<byte>.Shared.Rent(dstLen);

            try
            {
                fixed (byte* pSrcBase = src.Bytes)
                fixed (byte* pDstBase = dstBuffer)
                {
                    for (int row = 0; row < h; row++)
                    {
                        // 소스: (y + row) * stride + x
                        // 타겟: row * width
                        byte* pSrc = pSrcBase + (long)(y + row) * src.Stride + x;
                        byte* pDst = pDstBase + (long)row * dstStride;

                        // 한 줄 복사
                        Buffer.MemoryCopy(pSrc, pDst, dstStride, w);
                    }
                }

                // [핵심 개선] FrameData.Wrap을 사용하여 Dispose 시 자동으로 ArrayPool에 반환되도록 함
                // dstLen을 명시하여 풀에서 받은 버퍼 크기가 아닌 실제 유효 크기만 사용
                return FrameData.Wrap(dstBuffer, w, h, dstStride, dstLen);
            }
            catch
            {
                // 복사 중 에러 발생 시 버퍼 반환
                ArrayPool<byte>.Shared.Return(dstBuffer);
                throw;
            }
        }
    }
}
