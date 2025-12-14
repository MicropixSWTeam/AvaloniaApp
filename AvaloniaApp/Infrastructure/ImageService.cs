using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public class ImageService
    {
        public Mat CreateGrayMatFromMono8Frame(IFrame frame)
        {
            if (frame is null)
                throw new ArgumentNullException(nameof(frame));

            if (frame.PayloadType != IFrame.PayloadTypeValue.Image)
                throw new NotSupportedException(
                    $"Only Image payload is supported. PayloadType={frame.PayloadType}");

            if (frame.PixelFormat != IFrame.PixelFormatValue.Mono8)
                throw new NotSupportedException(
                    $"Only Mono8 is supported. PixelFormat={frame.PixelFormat}");

            int width = checked((int)frame.Width);
            int height = checked((int)frame.Height);

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Invalid frame size: {width}x{height}");

            if (frame.ImageData == IntPtr.Zero)
                throw new InvalidOperationException("ImageData pointer is null.");

            const int bytesPerPixel = 1;        // Mono8
            int stride = width * bytesPerPixel;
            int neededSize = stride * height;

            if (frame.BufferSize < (uint)neededSize)
            {
                throw new InvalidOperationException(
                    $"BufferSize is smaller than image size. BufferSize={frame.BufferSize}, Needed={neededSize}");
            }

            // Vimba 버퍼 → 관리 버퍼로 복사
            var buffer = new byte[neededSize];
            Marshal.Copy(frame.ImageData, buffer, 0, neededSize);

            // row-major, padding 없는 Gray8 데이터로 Mat 생성
            return Mat.FromPixelData(height, width, MatType.CV_8UC1, buffer);
        }

        public WriteableBitmap MatToGray8WriteableBitmap(Mat src)
        {
            if (src is null)
                throw new ArgumentNullException(nameof(src));

            if (src.Empty())
                throw new ArgumentException("Mat is empty.", nameof(src));

            if (src.Type() != MatType.CV_8UC1)
                throw new NotSupportedException($"Only CV_8UC1 is supported. Type={src.Type()}");

            int width = src.Cols;
            int height = src.Rows;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Invalid Mat size: {width}x{height}");

            var bmp = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            using (var fb = bmp.Lock())
            {
                IntPtr dstBase = fb.Address;
                int dstStride = fb.RowBytes;
                IntPtr srcBase = src.Data;
                int srcStride = (int)src.Step();

                int rowBytes = width; // Gray8: 1 byte per pixel

                // 공용 임시 버퍼 한 줄
                byte[] buffer = new byte[rowBytes];

                for (int y = 0; y < height; y++)
                {
                    IntPtr srcLine = srcBase + y * srcStride;
                    IntPtr dstLine = dstBase + y * dstStride;

                    Marshal.Copy(srcLine, buffer, 0, rowBytes);
                    Marshal.Copy(buffer, 0, dstLine, rowBytes);
                }
            }

            return bmp;
        }

        public Mat Crop(Mat mat, Rect rect)
        {
            return new Mat(mat, rect);
        }
    }
}
