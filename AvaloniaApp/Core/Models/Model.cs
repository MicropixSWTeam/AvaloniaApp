using Avalonia;
using AvaloniaApp.Core.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace AvaloniaApp.Core.Models
{
    public sealed class FrameData : IDisposable
    {
        private int _disposed;
        private readonly Action<byte[]>? _return;
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int Length { get; }
        public byte[] Bytes { get; }
        private FrameData(byte[] bytes, int width, int height, int stride, int length, Action<byte[]>? @return)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            Width = width;
            Height = height;
            Stride = stride;
            Length = length;
            _return = @return;

            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
            if (length <= 0 || length > bytes.Length) throw new ArgumentOutOfRangeException(nameof(length));
        }
        // 풀 반환용 delegate(매 프레임 람다 생성 방지)
        private static void ReturnToPool(byte[] b) => ArrayPool<byte>.Shared.Return(b);
        // 스트리밍용: 이미 Rent한 버퍼를 감싸기(Dispose 시 Return)
        public static FrameData Wrap(byte[] buffer, int width, int height, int stride, int length) => new FrameData(buffer, width, height, stride, length, ReturnToPool);
        // 스냅샷/소유: Dispose 시 반환 없음
        public static FrameData Own(byte[] buffer, int width, int height, int stride, int length) => new FrameData(buffer, width, height, stride, length, @return: null);
        // 다른 FrameData로부터 스냅샷(복사본) 생성
        public static FrameData CloneFullFrame(FrameData src)
        {
            var dst = new byte[src.Length];
            global::System.Buffer.BlockCopy(src.Bytes, 0, dst, 0, src.Length);
            return FrameData.Own(dst, src.Width, src.Height, src.Stride, src.Length);
        }
        public static FrameData CloneCropFrame(FrameData src, OpenCvSharp.Rect roi)
        {
            roi = Util.ClampRoi(roi, src.Width, src.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentException("Invalid ROI", nameof(roi));

            int w = roi.Width;
            int h = roi.Height;
            int dstStride = w;
            int dstLen = checked(w * h);

            var dst = new byte[dstLen];

            for (int y = 0; y < h; y++)
            {
                int srcOff = (roi.Y + y) * src.Stride + roi.X;
                int dstOff = y * dstStride;
                global::System.Buffer.BlockCopy(src.Bytes, srcOff, dst, dstOff, w);
            }

            return Own(dst, w, h, dstStride, dstLen);
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _return?.Invoke(Bytes);
        }
    }
    // 차트에 표시할 데이터
    public sealed record IntensityData(double mean,double stddev);
    public sealed class RegionData : IDisposable
    {
        public int Id { get; set; } 
        public int ColorIndex { get; set; }
        public Rect Rect { get; set; }
        public List<IntensityData> IntensityDatas { get; } = new();
        public void Dispose()
        {
            IntensityDatas.Clear();
            GC.SuppressFinalize(this);
        }
    }
    public sealed record Offset ( int offsetX, int offsetY);

    public sealed class ComboBoxData()
    {
        public string DisplayText { get; set; } = string.Empty;
        public int NumericValue { get; set; }
        public override string ToString() => DisplayText;
    }
}
