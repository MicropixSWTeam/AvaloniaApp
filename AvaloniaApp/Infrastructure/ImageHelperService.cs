using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Models;
using System;
using System.Runtime.InteropServices;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// 이미지 좌표 변환 및 FrameData 통계 계산을 담당하는 순수 도우미
    /// </summary>
    public class ImageHelperService
    {
        /// <summary>
        /// 화면상의 컨트롤 좌표(Rect)를 실제 이미지 픽셀 좌표(Rect)로 변환합니다.
        /// </summary>
        public Rect ControlRectToImageRect(Rect selectionInControl, Size controlSize, Bitmap bitmap)
        {
            var pixelSize = bitmap.PixelSize;

            if (controlSize.Width <= 0 || controlSize.Height <= 0 || pixelSize.Width <= 0 || pixelSize.Height <= 0)
                return new Rect();

            double scale = Math.Min(controlSize.Width / pixelSize.Width, controlSize.Height / pixelSize.Height);

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

            // 이미지 범위 내로 Clamp
            x1 = Math.Clamp(x1, 0, pixelSize.Width);
            x2 = Math.Clamp(x2, 0, pixelSize.Width);
            y1 = Math.Clamp(y1, 0, pixelSize.Height);
            y2 = Math.Clamp(y2, 0, pixelSize.Height);

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }

        /// <summary>
        /// [고성능] FrameData 바이트 배열에서 직접 통계(Mean, StdDev) 계산
        /// 메모리 할당 없이 포인터 연산만 수행하므로 매우 빠릅니다.
        /// </summary>
        public unsafe (double mean, double stdDev)? CalculateStatistics(FrameData frame, Rect imageRect)
        {
            if (frame == null) return null;

            int x = (int)imageRect.X;
            int y = (int)imageRect.Y;
            int w = (int)imageRect.Width;
            int h = (int)imageRect.Height;

            // 경계 보정
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > frame.Width) w = frame.Width - x;
            if (y + h > frame.Height) h = frame.Height - y;

            if (w <= 0 || h <= 0) return null;

            long sum = 0;
            long sumSq = 0;
            int count = w * h;

            fixed (byte* ptr = frame.Bytes)
            {
                for (int row = 0; row < h; row++)
                {
                    // Stride를 고려한 행 시작 주소
                    byte* rowPtr = ptr + ((y + row) * frame.Stride) + x;

                    for (int col = 0; col < w; col++)
                    {
                        byte val = rowPtr[col];
                        sum += val;
                        sumSq += (long)val * val;
                    }
                }
            }

            double mean = (double)sum / count;
            double variance = (double)sumSq / count - mean * mean;
            if (variance < 0) variance = 0;

            return (mean, Math.Sqrt(variance));
        }
    }
}