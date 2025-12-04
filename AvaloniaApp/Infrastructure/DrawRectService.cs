using Avalonia;
using Avalonia.Media.Imaging;
using System;

namespace AvaloniaApp.Infrastructure
{
    public class DrawRectService
    {
        /// <summary>
        /// 컨트롤 좌표계 selectionInControl 을
        /// 비트맵 픽셀 좌표계 사각형으로 변환 (Stretch=Uniform 기준).
        /// width==0 또는 height==0이면 "빈" 사각형으로 간주.
        /// </summary>
        public Rect ControlRectToImageRect(
            Rect selectionInControl,
            Size controlSize,
            Bitmap bitmap)
        {
            if (bitmap is null ||
                controlSize.Width <= 0 || controlSize.Height <= 0)
                return new Rect(0, 0, 0, 0);

            // 사용자가 클릭만 하고 거의 안 끌거나 한 픽셀도 안 되는 경우 방지
            if (selectionInControl.Width <= 0 || selectionInControl.Height <= 0)
                return new Rect(0, 0, 0, 0);

            var pixelSize = bitmap.PixelSize;
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
                return new Rect(0, 0, 0, 0);

            // Uniform scale
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

            if (x2 <= x1 || y2 <= y1)
                return new Rect(0, 0, 0, 0);

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }
    }
}
