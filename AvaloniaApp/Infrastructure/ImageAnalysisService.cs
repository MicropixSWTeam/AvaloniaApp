using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using OpenCvSharp;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Configuration;
using RectCv = OpenCvSharp.Rect;

namespace AvaloniaApp.Infrastructure
{
    public partial class ImageProcessService
    {
        public unsafe IntensityData[] GetIntensityDatas(FrameData fullFrame, Avalonia.Rect roiLocal, int wd)
        {
            // (기존 코드와 동일, Clamping 로직 포함되어 있어 안전함)
            var wavelengths = Options.GetWavelengthList();
            var waveToIndex = Options.GetWavelengthIndexMap();
            var tiles = Options.GetCoordinates(wd);

            int lx = (int)Math.Floor(roiLocal.X);
            int ly = (int)Math.Floor(roiLocal.Y);
            int lw = (int)Math.Ceiling(roiLocal.Width);
            int lh = (int)Math.Ceiling(roiLocal.Height);

            if (lw <= 0 || lh <= 0) return Array.Empty<IntensityData>();

            lx = Math.Clamp(lx, 0, Options.CropWidthSize - 1);
            ly = Math.Clamp(ly, 0, Options.CropHeightSize - 1);
            if (lx + lw > Options.CropWidthSize) lw = Options.CropWidthSize - lx;
            if (ly + lh > Options.CropHeightSize) lh = Options.CropHeightSize - ly;

            if (lw <= 0 || lh <= 0) return Array.Empty<IntensityData>();

            var result = new IntensityData[wavelengths.Count];

            for (int i = 0; i < wavelengths.Count; i++)
            {
                int wl = wavelengths[i];
                int tileIndex = waveToIndex[wl];
                var tile = tiles[tileIndex];

                int fx = tile.X + lx;
                int fy = tile.Y + ly;
                int fw = lw;
                int fh = lh;

                ClampToRect(ref fx, ref fy, ref fw, ref fh, tile);
                ClampToFrame(ref fx, ref fy, ref fw, ref fh, fullFrame.Width, fullFrame.Height);

                byte mean = 0, std = 0;
                if (fw > 0 && fh > 0)
                    (mean, std) = CalcMeanStdDev8(fullFrame, fx, fy, fw, fh);

                result[i] = new IntensityData(wl, mean, std);
            }
            return result;
        }

        // [최적화] 병렬 처리 적용된 버전
        public IReadOnlyDictionary<int, IntensityData[]> ComputeIntensityDataMap(
            FrameData fullFrame,
            IReadOnlyList<RegionData> regions,
            int wd)
        {
            if (regions is null || regions.Count == 0)
                return new Dictionary<int, IntensityData[]>();

            var resultMap = new ConcurrentDictionary<int, IntensityData[]>();

            Parallel.ForEach(regions, region =>
            {
                var datas = GetIntensityDatas(fullFrame, region.Rect, wd);
                resultMap[region.Index] = datas;
            });

            return new Dictionary<int, IntensityData[]>(resultMap);
        }

        private static void ClampToRect(ref int x, ref int y, ref int w, ref int h, RectCv bound)
        {
            if (x < bound.X) { w -= (bound.X - x); x = bound.X; }
            if (y < bound.Y) { h -= (bound.Y - y); y = bound.Y; }
            int maxX = bound.X + bound.Width;
            int maxY = bound.Y + bound.Height;
            if (x + w > maxX) w = maxX - x;
            if (y + h > maxY) h = maxY - y;
            if (w < 0) w = 0;
            if (h < 0) h = 0;
        }

        private static void ClampToFrame(ref int x, ref int y, ref int w, ref int h, int frameW, int frameH)
        {
            if (x < 0) { w -= -x; x = 0; }
            if (y < 0) { h -= -y; y = 0; }
            if (x + w > frameW) w = frameW - x;
            if (y + h > frameH) h = frameH - y;
            if (w < 0) w = 0;
            if (h < 0) h = 0;
        }

        private static unsafe (byte mean, byte stddev) CalcMeanStdDev8(FrameData frame, int x, int y, int w, int h)
        {
            long sum = 0;
            long sumSq = 0;
            int stride = frame.Stride;
            var bytes = frame.Bytes;

            fixed (byte* p0 = bytes)
            {
                for (int row = 0; row < h; row++)
                {
                    byte* p = p0 + (y + row) * stride + x;
                    for (int col = 0; col < w; col++)
                    {
                        byte v = p[col];
                        sum += v;
                        sumSq += (long)v * v;
                    }
                }
            }

            double n = (double)w * h;
            double m = sum / n;
            double var = (sumSq / n) - (m * m);
            if (var < 0) var = 0;
            double sd = Math.Sqrt(var);

            return ((byte)Math.Clamp((int)Math.Round(m), 0, 255), (byte)Math.Clamp((int)Math.Round(sd), 0, 255));
        }
    }
}