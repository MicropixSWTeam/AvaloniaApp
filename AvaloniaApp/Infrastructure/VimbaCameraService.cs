using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    public class VimbaCameraService : IAsyncDisposable
    {
        private readonly IVmbSystem _system;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IReadOnlyList<ICamera>? _cameras;
        private IOpenCamera? _openCamera;
        private IAcquisition? _acquisition;

        private bool _disposed;

        public CameraInfo? ConnectedCameraInfo { get; private set; }

        /// <summary>
        /// 연속 프리뷰용 프레임 이벤트.
        /// Bitmap 소유권은 구독자에게 넘어간다고 가정한다.
        /// (구독자가 Dispose 해줘야 함)
        /// </summary>
        public event EventHandler<Bitmap>? FrameReady;

        public VimbaCameraService()
        {
            _system = IVmbSystem.Startup();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VimbaCameraService));
        }

        public Task<IReadOnlyList<PixelFormatInfo>> GetSupportPixelformatListAsync(
            CancellationToken ct, string? id)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var camera = _system.GetCameraByID(id)
                         ?? throw new InvalidOperationException($"Camera '{id}' not found.");

            var list = new[]
            {
                new PixelFormatInfo(
                    name: "Mono8",
                    displayName: "Mono8",
                    isAvailable: true)
            };

            return Task.FromResult<IReadOnlyList<PixelFormatInfo>>(list);
        }

        public Task<IReadOnlyList<CameraInfo>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var cameras = _system.GetCameras();
            _cameras = cameras;

            var result = cameras
                .Select(c => new CameraInfo(
                    id: c.Id,
                    name: c.Name,
                    serial: c.Serial,
                    modelName: c.ModelName))
                .ToArray();

            return Task.FromResult<IReadOnlyList<CameraInfo>>(result);
        }

        public async Task ConnectAsync(CancellationToken ct, string id)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                // 이미 같은 카메라에 연결되어 있으면 패스
                if (_openCamera is not null && ConnectedCameraInfo?.Id == id)
                    return;

                // 이전 스트림 정리
                _acquisition?.Dispose();
                _acquisition = null;

                if (_openCamera is not null)
                {
                    _openCamera.FrameReceived -= OnFrameReceived;
                    _openCamera.Dispose();
                    _openCamera = null;
                }

                ConnectedCameraInfo = null;

                var camera = _system.GetCameraByID(id)
                             ?? throw new InvalidOperationException($"Camera '{id}' not found.");

                _openCamera = camera.Open();

                ConnectedCameraInfo = new CameraInfo(
                    id: camera.Id,
                    name: camera.Name,
                    serial: camera.Serial,
                    modelName: camera.ModelName);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                _acquisition?.Dispose();
                _acquisition = null;

                if (_openCamera is not null)
                {
                    _openCamera.FrameReceived -= OnFrameReceived;
                    _openCamera.Dispose();
                    _openCamera = null;
                }

                ConnectedCameraInfo = null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task StartAsync(CancellationToken ct) => StartStreamAsync(ct);
        public Task StopAsync(CancellationToken ct) => StopStreamAsync(ct);

        /// <summary>
        /// 연속 스트림 시작 (FrameReady 이벤트로 Bitmap 푸시)
        /// </summary>
        public async Task StartStreamAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                if (_openCamera is null)
                    throw new InvalidOperationException("카메라가 연결되지 않았습니다.");

                if (_acquisition is not null)
                    return; // 이미 스트리밍 중

                _openCamera.FrameReceived += OnFrameReceived;
                _acquisition = _openCamera.StartFrameAcquisition();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task StopStreamAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                if (_acquisition is not null)
                {
                    _acquisition.Dispose();
                    _acquisition = null;
                }

                if (_openCamera is not null)
                {
                    _openCamera.FrameReceived -= OnFrameReceived;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Vimba 연속 프레임 콜백 → Bitmap 생성 후 FrameReady 이벤트로 전달
        /// </summary>
        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            try
            {
                using var frame = e.Frame;

                if (frame.FrameStatus != IFrame.FrameStatusValue.Completed)
                    return;
                if (frame.PayloadType != IFrame.PayloadTypeValue.Image)
                    return;
                if (frame.PixelFormat != IFrame.PixelFormatValue.Mono8)
                    return;

                int width = checked((int)frame.Width);
                int height = checked((int)frame.Height);

                const int bytesPerPixel = 1;
                int stride = width * bytesPerPixel;
                int imageSize = stride * height;

                if (frame.ImageData == IntPtr.Zero)
                    return;
                if (frame.BufferSize < (uint)imageSize)
                    return;

                var buffer = new byte[imageSize];
                Marshal.Copy(frame.ImageData, buffer, 0, imageSize);

                var bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormats.Gray8,
                    AlphaFormat.Opaque);

                using (var fb = bitmap.Lock())
                {
                    int destStride = fb.RowBytes;

                    if (destStride == stride)
                    {
                        Marshal.Copy(buffer, 0, fb.Address, imageSize);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr destLine = IntPtr.Add(fb.Address, y * destStride);
                            Marshal.Copy(buffer, y * stride, destLine, stride);
                        }
                    }
                }

                if (FrameReady is null)
                {
                    // 구독자 없으면 즉시 Dispose (메모리 누수 방지)
                    bitmap.Dispose();
                    return;
                }

                FrameReady?.Invoke(this, bitmap);
            }
            catch
            {
                // 이벤트 핸들러에서 예외 전파 방지
            }
        }

        /// <summary>
        /// 단일 캡처: Bitmap 반환 (소유권은 호출자)
        /// </summary>
        public async Task<Bitmap> CaptureAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                if (_openCamera is null)
                    throw new InvalidOperationException("카메라가 연결되지 않았습니다.");

                using IFrame frame = _openCamera.AcquireSingleImage(TimeSpan.FromMilliseconds(500));

                if (frame.FrameStatus != IFrame.FrameStatusValue.Completed)
                    throw new InvalidOperationException($"프레임 상태가 Completed가 아님: {frame.FrameStatus}");
                if (frame.PayloadType != IFrame.PayloadTypeValue.Image)
                    throw new InvalidOperationException($"PayloadType이 Image가 아님: {frame.PayloadType}");
                if (frame.PixelFormat != IFrame.PixelFormatValue.Mono8)
                    throw new InvalidOperationException($"PixelFormat이 Mono8이 아님: {frame.PixelFormat}");

                int width = checked((int)frame.Width);
                int height = checked((int)frame.Height);

                const int bytesPerPixel = 1;
                int stride = width * bytesPerPixel;
                int imageSize = stride * height;

                if (frame.ImageData == IntPtr.Zero)
                    throw new InvalidOperationException("ImageData 포인터가 null 입니다.");
                if (frame.BufferSize < imageSize)
                    throw new InvalidOperationException(
                        $"버퍼 크기 부족: BufferSize={frame.BufferSize}, 필요={imageSize}");

                var buffer = new byte[imageSize];
                Marshal.Copy(frame.ImageData, buffer, 0, imageSize);

                var bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormats.Gray8,
                    AlphaFormat.Opaque);

                using (var fb = bitmap.Lock())
                {
                    int destStride = fb.RowBytes;

                    if (destStride == stride)
                    {
                        Marshal.Copy(buffer, 0, fb.Address, imageSize);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr destLine = IntPtr.Add(fb.Address, y * destStride);
                            Marshal.Copy(buffer, y * stride, destLine, stride);
                        }
                    }
                }

                return bitmap;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _acquisition?.Dispose();
                _acquisition = null;

                if (_openCamera is not null)
                {
                    _openCamera.FrameReceived -= OnFrameReceived;
                    _openCamera.Dispose();
                    _openCamera = null;
                }

                _system.Shutdown();
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
