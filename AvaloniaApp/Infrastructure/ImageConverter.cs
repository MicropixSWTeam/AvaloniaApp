using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using VmbNET;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public class ImageConverter
    {
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

                // 스트라이드가 다른 경우 (Row-by-Row 복사)
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
    }
}
