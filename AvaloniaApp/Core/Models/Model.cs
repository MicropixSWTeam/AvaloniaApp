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
    /// <summary>
    /// ArrayPool에서 빌린 프레임 버퍼(Gray8, packed).
    /// 반드시 Dispose를 호출하여 버퍼를 반환해야 함.
    /// </summary>
    public sealed class FramePacket : IDisposable
    {
        private int _returned;
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int Length { get; }
        public byte[] Buffer { get; }
        public FramePacket(byte[] buffer, int width, int height, int stride, int length)
        {
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Width = width;
            Height = height;
            Stride = stride;
            Length = length;
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _returned, 1) == 1)
                return;

            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
    public sealed class FrameSnapshot
    {
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public byte[] Buffer { get; }
        public FrameSnapshot(int w, int h, int stride, byte[] buf)
        {
            Width = w; Height = h; Stride = stride; Buffer = buf;
        }
    }
}
