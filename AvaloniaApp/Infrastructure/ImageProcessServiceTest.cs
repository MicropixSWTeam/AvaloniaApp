using Avalonia;
using Avalonia.Media.Imaging;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure
{
    public class ImageProcessServiceTest
    {
        private Options _options;
        public ImageProcessServiceTest(Options options)
        {
            _options = options;
        }
        public unsafe void ConvertFrameDataToWriteableBitmap(WriteableBitmap bitmap, FrameData frame)
        {
            using var fb = bitmap.Lock();

            int width = frame.Width;
            int height = frame.Height;

            int srcStride = frame.Stride;     // packed라면 width
            int dstStride = fb.RowBytes;
            int rowBytes = width;             // Gray8

            fixed (byte* src0 = frame.Bytes)
            {
                byte* src = src0;
                byte* dst = (byte*)fb.Address;

                if (dstStride == srcStride)
                {
                    // 최적화된 블록 복사 (packed to packed)
                    Buffer.MemoryCopy(src, dst, (long)dstStride * height, frame.Length);
                    return;
                }

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * srcStride,
                        dst + (long)y * dstStride,
                        rowBytes,
                        rowBytes);
                }
            }
        }
        public unsafe void ImageProcess(FrameData frame)
        {
            fixed(byte*p = frame.Bytes)
            {
                using var mat = OpenCvSharp.Mat.FromPixelData(frame.Height, frame.Width, OpenCvSharp.MatType.CV_8UC1, frame.Bytes, frame.Stride);
            }
        }
        public async Task<List<FrameData>> GetCropFrameDatas(FrameData frame,OpenCvSharp.Rect[] coordinates)
        {
            var frames = new List<FrameData>();
            foreach (var rect in coordinates)
            {
                var cropFrame = FrameData.CloneCropFrame(frame, rect);
                ImageProcess(cropFrame);
                frames.Add(cropFrame);
            }
            return frames;
        }

        public async Task<FrameData> GetStitchFrameData(List<FrameData> frames)
        {
            // frames
            return frames.First();
        }

        public void Translation(FrameData frame,Offset offset)
        {
            // 기존의 coordinates 는 지금은 아니지만 나중에 distance값에 따른  translation을 적용한 corrdinates가 될거임 그럼 그 offset만큼 조정할필요가 있을듯 즉 이동해서 잘랐으니 겹치는 부분만큼 추려서 가운데에맞추고 나머지는 원래 사이즈에 맞게 return하는 무슨 느낌이지 이해감?
        }
    }
}
