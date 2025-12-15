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
    public class ImageProcessor
    {
        public unsafe FrameSnapshot FrameToBitmap(WriteableBitmap bmp)
        {
            using var fb = bmp.Lock();
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            int stride = fb.RowBytes;

            var dst = new byte[stride * h];

            fixed (byte* d0 = dst)
            {
                Buffer.MemoryCopy((void*)fb.Address, d0, dst.Length, dst.Length);
            }

            return new FrameSnapshot(w, h, stride, dst);
        }
        public unsafe void UpdateWriteableBitmap(WriteableBitmap target, FramePacket packet)
        {
            using var fb = target.Lock();

            int width = packet.Width;
            int height = packet.Height;

            int srcStride = packet.Stride;     // packed라면 width
            int dstStride = fb.RowBytes;
            int rowBytes = width;             // Gray8

            fixed (byte* src0 = packet.Buffer)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                if (dstStride == srcStride)
                {
                    // 최적화된 블록 복사 (packed to packed)
                    Buffer.MemoryCopy(src, dst, (long)dstStride * height, packet.Length);
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
        public Mat Crop(Mat mat, Rect rect)
        {
            return new Mat(mat, rect);
        }
    }
}
