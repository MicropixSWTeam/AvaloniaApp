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
        public static FrameData CloneOwned(FrameData src)
        {
            var dst = new byte[src.Length];
            global::System.Buffer.BlockCopy(src.Bytes, 0, dst, 0, src.Length);
            return FrameData.Own(dst, src.Width, src.Height, src.Stride, src.Length);
        }
        public static FrameData CropCopyOwned(FrameData src, OpenCvSharp.Rect roi)
        {
            roi = ClampRoi(roi, src.Width, src.Height);
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
        static OpenCvSharp.Rect ClampRoi(OpenCvSharp.Rect r, int w, int h)
        {
            int x1 = Math.Clamp(r.X, 0, w);
            int y1 = Math.Clamp(r.Y, 0, h);
            int x2 = Math.Clamp(r.X + r.Width, 0, w);
            int y2 = Math.Clamp(r.Y + r.Height, 0, h);
            return new OpenCvSharp.Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _return?.Invoke(Bytes);
        }
    }
    public sealed record ChartData
    {
        public double Mean { get; set; }
        public double StdDev { get; set; }  
    }
    public sealed class WorkSpace : IDisposable
    {
        private bool disposedValue;

        public FrameData? Entire { get; set; }
        public List<FrameData> Crops { get; set; } = new();

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 관리형 상태(관리형 개체)를 삭제합니다.
                }

                // TODO: 비관리형 리소스(비관리형 개체)를 해제하고 종료자를 재정의합니다.
                // TODO: 큰 필드를 null로 설정합니다.
                disposedValue = true;
            }
        }

        // // TODO: 비관리형 리소스를 해제하는 코드가 'Dispose(bool disposing)'에 포함된 경우에만 종료자를 재정의합니다.
        // ~WorkSpace()
        // {
        //     // 이 코드를 변경하지 마세요. 'Dispose(bool disposing)' 메서드에 정리 코드를 입력합니다.
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 이 코드를 변경하지 마세요. 'Dispose(bool disposing)' 메서드에 정리 코드를 입력합니다.
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
