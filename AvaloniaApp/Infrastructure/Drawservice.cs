// AvaloniaApp.Infrastructure/DrawRectService.cs
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System;
using System.Runtime.InteropServices;

namespace AvaloniaApp.Infrastructure
{
    public class Drawservice
    {
        /// <summary>
        /// 컨트롤 좌표계에서의 선택 사각형(selectionInControl)을
        /// 비트맵 픽셀 좌표계 사각형으로 변환한다.
        /// Image.Stretch = Uniform 기준.
        /// </summary>
        public Rect ControlRectToImageRect(
            Rect selectionInControl,
            Size controlSize,
            Bitmap bitmap)
        {
            var pixelSize = bitmap.PixelSize; // px 단위

            if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
                pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                return new Rect(); // Width/Height == 0 -> 선택 없음 취급
            }

            // Uniform 스케일 팩터
            double scale = Math.Min(
                controlSize.Width / pixelSize.Width,
                controlSize.Height / pixelSize.Height);

            double imgWidth = pixelSize.Width * scale;
            double imgHeight = pixelSize.Height * scale;

            // 렌더된 이미지가 컨트롤 안에서 차지하는 좌상단 오프셋
            double offsetX = (controlSize.Width - imgWidth) * 0.5;
            double offsetY = (controlSize.Height - imgHeight) * 0.5;

            // 컨트롤 좌표 → 이미지(픽셀) 좌표
            Point c1 = selectionInControl.TopLeft;
            Point c2 = selectionInControl.BottomRight;

            double x1 = (c1.X - offsetX) / scale;
            double y1 = (c1.Y - offsetY) / scale;
            double x2 = (c2.X - offsetX) / scale;
            double y2 = (c2.Y - offsetY) / scale;

            // 클램프 (이미지 범위 밖 드래그 방지)
            x1 = Math.Clamp(x1, 0, pixelSize.Width);
            x2 = Math.Clamp(x2, 0, pixelSize.Width);
            y1 = Math.Clamp(y1, 0, pixelSize.Height);
            y2 = Math.Clamp(y2, 0, pixelSize.Height);

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }

        /// <summary>
        /// 선택 사각형(컨트롤 좌표)을 기준으로,
        /// 해당 이미지 영역의 Y(mean/stdDev)를 계산.
        /// (원본 Bitmap, BGRA 기준)
        /// </summary>
        public (double mean, double stdDev)? GetYStatsFromSelection(
            Bitmap bitmap,
            Rect selectionInControl,
            Size controlSize)
        {
            if (bitmap is null) return null;

            var imageRect = ControlRectToImageRect(selectionInControl, controlSize, bitmap);
            if (imageRect.Width <= 0 || imageRect.Height <= 0)
                return null;

            return GetYStatsFromImageRect(bitmap, imageRect);
        }

        /// <summary>
        /// 이미지 픽셀 좌표계(Rect가 이미지 좌표라고 가정)에 대해
        /// Y(mean/stdDev)를 계산. (BGRA 등 일반 Bitmap용)
        /// </summary>
        public (double mean, double stdDev)? GetYStatsFromImageRect(
            Bitmap bitmap,
            Rect imageRect)
        {
            if (bitmap is null) return null;

            var pixelSize = bitmap.PixelSize;
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
                return null;

            int x = (int)Math.Floor(imageRect.X);
            int y = (int)Math.Floor(imageRect.Y);
            int w = (int)Math.Ceiling(imageRect.Width);
            int h = (int)Math.Ceiling(imageRect.Height);

            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }

            if (x + w > pixelSize.Width)
                w = pixelSize.Width - x;
            if (y + h > pixelSize.Height)
                h = pixelSize.Height - y;

            if (w <= 0 || h <= 0)
                return null;

            var sourceRect = new PixelRect(x, y, w, h); // Avalonia.PixelRect

            int stride = w * 4; // BGRA8888
            int bufferSize = stride * h;
            var buffer = new byte[bufferSize];

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(
                    sourceRect,
                    handle.AddrOfPinnedObject(),
                    bufferSize,
                    stride);
            }
            finally
            {
                handle.Free();
            }

            return ComputeYStatsFromBgraBuffer(buffer, w, h);
        }

        /// <summary>
        /// Gray8 WriteableBitmap에서, imageRect(픽셀 좌표)에 해당하는 영역의
        /// 평균/표준편차를 계산한다.
        /// imageRect는 타일 내부 좌표계(0..Width, 0..Height) 기준.
        /// unsafe 없이 동작하도록 구현.
        /// </summary>
        public (double mean, double stdDev)? GetYStatsFromGrayTile(
            WriteableBitmap tile,
            Rect imageRect)
        {
            if (tile is null) return null;

            var ps = tile.PixelSize;
            if (ps.Width <= 0 || ps.Height <= 0)
                return null;

            int x = (int)Math.Floor(imageRect.X);
            int y = (int)Math.Floor(imageRect.Y);
            int w = (int)Math.Ceiling(imageRect.Width);
            int h = (int)Math.Ceiling(imageRect.Height);

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

            if (x + w > ps.Width)
                w = ps.Width - x;
            if (y + h > ps.Height)
                h = ps.Height - y;

            if (w <= 0 || h <= 0)
                return null;

            var sourceRect = new PixelRect(x, y, w, h);

            // Gray8: 1 byte per pixel
            int stride = w;
            int bufferSize = stride * h;
            var buffer = new byte[bufferSize];

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                tile.CopyPixels(
                    sourceRect,
                    handle.AddrOfPinnedObject(),
                    bufferSize,
                    stride);
            }
            finally
            {
                handle.Free();
            }

            int count = w * h;
            if (count == 0)
                return null;

            long sum = 0;
            long sumSq = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                byte v = buffer[i];
                sum += v;
                sumSq += (long)v * v;
            }

            double mean = (double)sum / count;
            double variance = (double)sumSq / count - mean * mean;
            if (variance < 0) variance = 0;
            double std = Math.Sqrt(variance);

            return (mean, std);
        }

        /// <summary>
        /// BGRA 버퍼에서 Y(mean/stdDev) 계산 (기존 구현 그대로 유지)
        /// </summary>
        private static (double mean, double stdDev) ComputeYStatsFromBgraBuffer(
            byte[] buffer,
            int w,
            int h)
        {
            double sum = 0.0;
            double sumSq = 0.0;
            int pixelCount = w * h;

            for (int i = 0; i < buffer.Length; i += 4)
            {
                byte b = buffer[i + 0];
                byte g = buffer[i + 1];
                byte r = buffer[i + 2];

                // OpenCV COLOR_BGRA2GRAY와 같은 BT.601 근사
                double yVal = 0.114 * b + 0.587 * g + 0.299 * r;

                sum += yVal;
                sumSq += yVal * yVal;
            }

            double mean = sum / pixelCount;
            double variance = Math.Max(0.0, sumSq / pixelCount - mean * mean);
            double stdDev = Math.Sqrt(variance);

            return (mean, stdDev);
        }

    }
}
