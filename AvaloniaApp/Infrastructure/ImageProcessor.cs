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
        public static Rect ClampRoi(Rect r, int w, int h)
        {
            int x1 = Math.Clamp(r.X, 0, w);
            int y1 = Math.Clamp(r.Y, 0, h);
            int x2 = Math.Clamp(r.X + r.Width, 0, w);
            int y2 = Math.Clamp(r.Y + r.Height, 0, h);
            return new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }
        public unsafe void CropRectExtract(FrameData frame, Rect roi)
        {
            roi = ClampRoi(roi, frame.Width, frame.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return;


            fixed (byte* p0 = frame.Bytes)
            {
                using var full = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, (IntPtr)p0, frame.Stride);
                using var roiMat = new Mat(full, roi); // view
            }
        }
        public static FrameData CropRectCopy(FrameData src, Rect roi)
        {
            roi = ClampRoi(roi, src.Width, src.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentException("Invalid ROI", nameof(roi));

            int w = roi.Width;
            int h = roi.Height;
            int dstStride = w;
            int dstLen = checked(dstStride * h);

            var dst = new byte[dstLen];

            for (int y = 0; y < h; y++)
            {
                int srcOff = (roi.Y + y) * src.Stride + roi.X;
                int dstOff = y * dstStride;
                global::System.Buffer.BlockCopy(src.Bytes, srcOff, dst, dstOff, w);
            }

            return FrameData.Own(dst, w, h, dstStride, dstLen);
        }
    }
}
