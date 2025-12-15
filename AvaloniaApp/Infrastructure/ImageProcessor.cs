using Avalonia;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public class ImageProcessor
    {
        public static Rect ClampRoi(Rect r,int w,int h)
        {             
            int x = Math.Max(r.X, 0);
            int y = Math.Max(r.Y, 0);
            int width = Math.Min(r.Width, w - x);
            int height = Math.Min(r.Height, h - y);
            return new Rect(x, y, width, height);
        }
    }
}
