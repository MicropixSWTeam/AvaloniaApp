using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
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
    }
}