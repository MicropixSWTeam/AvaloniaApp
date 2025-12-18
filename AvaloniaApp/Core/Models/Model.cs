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

        // [GC 최적화] 풀 반환용 delegate
        private static void ReturnToPool(byte[] b) => ArrayPool<byte>.Shared.Return(b);

        // [스트리밍용] 외부에서 Rent한 버퍼를 감쌀 때 (Dispose 시 Return)
        public static FrameData Wrap(byte[] buffer, int width, int height, int stride, int length)
            => new FrameData(buffer, width, height, stride, length, ReturnToPool);

        // [소유권용] 특정 버퍼를 영구 소유하거나 별도 관리할 때 (Dispose 시 반환 안 함)
        public static FrameData Own(byte[] buffer, int width, int height, int stride, int length)
            => new FrameData(buffer, width, height, stride, length, @return: null);

        // [GC 최적화] ArrayPool을 사용하여 FullFrame 복사
        public static FrameData CloneFullFrame(FrameData src)
        {
            // new byte[] 대신 Rent 사용
            var dst = ArrayPool<byte>.Shared.Rent(src.Length);
            try
            {
                global::System.Buffer.BlockCopy(src.Bytes, 0, dst, 0, src.Length);
                return new FrameData(dst, src.Width, src.Height, src.Stride, src.Length, ReturnToPool);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(dst);
                throw;
            }
        }

        // [GC 최적화] ArrayPool을 사용하여 Crop 복사
        public static FrameData CloneCropFrame(FrameData src, OpenCvSharp.Rect roi)
        {
            // roi 검증 로직 (Util 클래스 의존성 제거됨)
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentException("Invalid ROI", nameof(roi));

            int w = roi.Width;
            int h = roi.Height;
            int dstStride = w;
            int dstLen = checked(w * h);

            // 중요: 정확한 사이즈가 아니라 넉넉한 사이즈를 빌려옴
            var dst = ArrayPool<byte>.Shared.Rent(dstLen);

            try
            {
                for (int y = 0; y < h; y++)
                {
                    int srcOff = (roi.Y + y) * src.Stride + roi.X;
                    int dstOff = y * dstStride;
                    // BlockCopy는 byte 단위
                    global::System.Buffer.BlockCopy(src.Bytes, srcOff, dst, dstOff, w);
                }

                // FrameData 생성 시 dstLen을 명시하여, 실제 유효한 데이터 길이만 사용
                return new FrameData(dst, w, h, dstStride, dstLen, ReturnToPool);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(dst);
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _return?.Invoke(Bytes);
        }
    }

    public sealed record IntensityData(byte mean, byte stddev);
    public class SelectRegionData
    {
        public int Index { get; set; }
        public Rect ControlRect { get; set; }
        public Rect Rect { get; set; }
        public byte Mean { get; set; }
        public byte StdDev { get; set; }
        public int ColorIndex { get; set; }
    }
    public sealed record Offset(int offsetX, int offsetY);
    
    public sealed class ComboBoxData()
    {
        public string DisplayText { get; set; } = string.Empty;
        public int NumericValue { get; set; }
        public override string ToString() => DisplayText;
    }
}