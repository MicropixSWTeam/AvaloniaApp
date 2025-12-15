using Avalonia.Media.Imaging;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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
        public static FrameData Rent(byte[] buffer, int width, int height, int stride, int length) => new FrameData(buffer, width, height, stride, length, ReturnToPool);
        // 스냅샷/소유: Dispose 시 반환 없음
        public static FrameData Own(byte[] buffer, int width, int height, int stride, int length) => new FrameData(buffer, width, height, stride, length, @return: null);
        // 다른 FrameData로부터 스냅샷(복사본) 생성
        public static unsafe FrameData Clone(FrameData src)
        {
            var dst = new byte[src.Length];

            fixed (byte* s0 = src.Bytes)
            fixed (byte* d0 = dst)
            {
                Buffer.MemoryCopy(s0, d0, dst.Length, src.Length);
            }

            return Own(dst, src.Width, src.Height, src.Stride, src.Length);
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
}
