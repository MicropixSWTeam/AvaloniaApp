using Avalonia.Platform;
using HarfBuzzSharp;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Core.Utils
{
    public static class Utils
    {
        // Avalonia.Rect (double) → OpenCvSharp.Rect (int)
        public static OpenCvSharp.Rect ToCvRect(Avalonia.Rect rect, int maxWidth, int maxHeight)
        {
            int x = (int)Math.Floor(rect.X);
            int y = (int)Math.Floor(rect.Y);
            int w = (int)Math.Ceiling(rect.Width);
            int h = (int)Math.Ceiling(rect.Height);

            // 영상 범위 밖으로 나가지 않도록 클램프
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > maxWidth) w = maxWidth - x;
            if (y + h > maxHeight) h = maxHeight - y;
            if (w < 0) w = 0;
            if (h < 0) h = 0;

            return new OpenCvSharp.Rect(x, y, w, h);
        }

        // OpenCvSharp.Rect → Avalonia.Rect (필요한 경우)
        public static Avalonia.Rect ToAvaloniaRect(OpenCvSharp.Rect r)
            => new Avalonia.Rect(r.X, r.Y, r.Width, r.Height);
    }
}
