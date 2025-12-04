using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Runtime.InteropServices;

namespace AvaloniaApp.Infrastructure
{
    public class DrawRectService
    {
        public Rect ControlRectToImageRect(
            Rect selectionInControl,
            Size controlSize,
            Bitmap bitmap)
        {
            var pixelSize = bitmap.PixelSize;

            if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
                pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                return new Rect(); // Width/Height == 0 → 선택 없음
            }

            double scale = Math.Min(
                controlSize.Width / pixelSize.Width,
                controlSize.Height / pixelSize.Height);

            double imgWidth = pixelSize.Width * scale;
            double imgHeight = pixelSize.Height * scale;

            double offsetX = (controlSize.Width - imgWidth) * 0.5;
            double offsetY = (controlSize.Height - imgHeight) * 0.5;

            Point c1 = selectionInControl.TopLeft;
            Point c2 = selectionInControl.BottomRight;

            double x1 = (c1.X - offsetX) / scale;
            double y1 = (c1.Y - offsetY) / scale;
            double x2 = (c2.X - offsetX) / scale;
            double y2 = (c2.Y - offsetY) / scale;

            x1 = Math.Clamp(x1, 0, pixelSize.Width);
            x2 = Math.Clamp(x2, 0, pixelSize.Width);
            y1 = Math.Clamp(y1, 0, pixelSize.Height);
            y2 = Math.Clamp(y2, 0, pixelSize.Height);

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }

        /// <summary>
        /// 선택 사각형(컨트롤 좌표)을 기준으로,
        /// 해당 이미지 영역의 Y(밝기) 평균/표준편차 계산.
        /// </summary>
        public (double mean, double stdDev)? GetYStatsFromSelection(
            Bitmap bitmap,
            Rect selectionInControl,
            Size controlSize)
        {
            if (bitmap is null)
                return null;

            var imageRect = ControlRectToImageRect(selectionInControl, controlSize, bitmap);
            if (imageRect.Width <= 0 || imageRect.Height <= 0)
                return null;

            var pixelSize = bitmap.PixelSize;

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

            var sourceRect = new PixelRect(x, y, w, h);

            int stride = w * 4;          // BGRA 4바이트
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

            double sum = 0.0;
            double sumSq = 0.0;
            int pixelCount = w * h;

            // BGRA 8:8:8:8
            for (int i = 0; i < buffer.Length; i += 4)
            {
                byte b = buffer[i + 0];
                byte g = buffer[i + 1];
                byte r = buffer[i + 2];

                double yVal = 0.114 * b + 0.587 * g + 0.299 * r;

                sum += yVal;
                sumSq += yVal * yVal;
            }

            double mean = sum / pixelCount;
            double variance = Math.Max(0.0, sumSq / pixelCount - mean * mean);
            double stdDev = Math.Sqrt(variance);

            return (mean, stdDev);
        }
        public Rect GetRenderedImageRect(Size controlSize, Bitmap bitmap)
        {
            var pixelSize = bitmap.PixelSize;

            if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
                pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                return new Rect();
            }

            double scale = Math.Min(
                controlSize.Width / pixelSize.Width,
                controlSize.Height / pixelSize.Height);

            double imgWidth = pixelSize.Width * scale;
            double imgHeight = pixelSize.Height * scale;

            double offsetX = (controlSize.Width - imgWidth) * 0.5;
            double offsetY = (controlSize.Height - imgHeight) * 0.5;

            return new Rect(offsetX, offsetY, imgWidth, imgHeight);
        }

    }
}
