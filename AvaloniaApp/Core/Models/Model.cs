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
    public sealed class FramePacket : IDisposable
    {
        private byte[]? _buffer;


        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int Length { get; }


        public FramePacket(byte[] buffer, int width, int height, int stride, int length)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Width = width;
            Height = height;
            Stride = stride;
            Length = length;
        }


        public ReadOnlySpan<byte> Span
        {
            get
            {
                var b = _buffer;
                return b is null ? ReadOnlySpan<byte>.Empty : b.AsSpan(0, Length);
            }
        }


        public void Dispose()
        {
            var b = _buffer;
            if (b is null) return;
            _buffer = null;
            ArrayPool<byte>.Shared.Return(b);
        }
    }
    public sealed class ImageWorkspace : IAsyncDisposable
    {
        private readonly object _lock = new();

        private Mat? _entire;
        private List<Mat> _crops = new();
        private List<Mat> _calibratedCrops = new();
        private Mat? _stitched;

        public long Version { get; private set; }

        public void ReplaceEntire(Mat mat, bool takeOwnership = true)
        {
            if (mat is null) throw new ArgumentNullException(nameof(mat));

            lock (_lock)
            {
                var old = _entire;
                _entire = takeOwnership ? mat : mat.Clone();
                old?.Dispose();
                Version++;
            }
        }

        public void ReplaceCrops(IReadOnlyList<Mat> mats, bool takeOwnership = true)
        {
            if (mats is null) throw new ArgumentNullException(nameof(mats));

            lock (_lock)
            {
                DisposeAll(_crops);
                _crops = takeOwnership ? new List<Mat>(mats) : CloneDeep(mats);
                Version++;
            }
        }

        public void ReplaceCalibratedCrops(IReadOnlyList<Mat> mats, bool takeOwnership = true)
        {
            if (mats is null) throw new ArgumentNullException(nameof(mats));

            lock (_lock)
            {
                DisposeAll(_calibratedCrops);
                _calibratedCrops = takeOwnership ? new List<Mat>(mats) : CloneDeep(mats);
                Version++;
            }
        }

        public void ReplaceStitched(Mat mat, bool takeOwnership = true)
        {
            if (mat is null) throw new ArgumentNullException(nameof(mat));

            lock (_lock)
            {
                var old = _stitched;
                _stitched = takeOwnership ? mat : mat.Clone();
                old?.Dispose();
                Version++;
            }
        }

        public Mat? GetEntireShared()
        {
            lock (_lock)
            {
                return _entire is null ? null : new Mat(_entire);
            }
        }

        public IReadOnlyList<Mat> GetCalibratedCropsShared()
        {
            lock (_lock)
            {
                return CloneHeaders(_calibratedCrops);
            }
        }

        public IReadOnlyList<Mat> GetCropsShared()
        {
            lock (_lock)
            {
                return CloneHeaders(_crops);
            }
        }

        public Mat? GetStitchedShared()
        {
            lock (_lock)
            {
                return _stitched is null ? null : new Mat(_stitched);
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _entire?.Dispose();
                _entire = null;

                DisposeAll(_crops);
                _crops.Clear();

                DisposeAll(_calibratedCrops);
                _calibratedCrops.Clear();

                _stitched?.Dispose();
                _stitched = null;

                Version++;
            }
        }

        private static void DisposeAll(List<Mat> mats)
        {
            foreach (var m in mats) m.Dispose();
        }

        // 깊은 복사(데이터 복제) — 외부가 소유권을 유지해야 하는 경우에만 사용
        private static List<Mat> CloneDeep(IReadOnlyList<Mat> mats)
        {
            var list = new List<Mat>(mats.Count);
            for (int i = 0; i < mats.Count; i++)
                list.Add(mats[i].Clone());
            return list;
        }

        // 얕은 복사(헤더만) — 외부로 안전하게 노출
        private static List<Mat> CloneHeaders(List<Mat> mats)
        {
            var list = new List<Mat>(mats.Count);
            for (int i = 0; i < mats.Count; i++)
                list.Add(new Mat(mats[i]));
            return list;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
