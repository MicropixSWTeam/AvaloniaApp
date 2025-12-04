using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
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
        /// Bitmap 소유권은 구독자에게 넘어감 (구독자가 Dispose 책임).
        /// </summary>
        public event EventHandler<Bitmap>? FrameReady;

        public bool IsStreaming => _acquisition is not null;

        public VimbaCameraService()
        {
            _system = IVmbSystem.Startup();
        }

        private IOpenCamera EnsureOpenCamera()
        {
            if (_openCamera is null)
                throw new InvalidOperationException("카메라가 연결되지 않았습니다.");
            return _openCamera;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VimbaCameraService));
        }

        public Task<IReadOnlyList<PixelFormatInfo>> GetSupportPixelformatListAsync(CancellationToken ct, string? id)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var camera = _system.GetCameraByID(id)
                         ?? throw new InvalidOperationException($"Camera '{id}' not found.");

            // 지금은 Mono8만 사용 (나중에 확장)
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

                if (_openCamera is not null && ConnectedCameraInfo?.Id == id)
                    return;

                // 스트림/이벤트 정리
                SafeStopAcquisition_NoLock();

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
                // 프레임 이벤트 핸들러는 StartStream에서 붙임

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

                SafeStopAcquisition_NoLock();

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

        public async Task StartStreamAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                var cam = EnsureOpenCamera();

                if (_acquisition is not null)
                    return; // 이미 스트리밍 중

                cam.FrameReceived += OnFrameReceived;
                // VmbNET 권장 방법: StartFrameAcquisition 호출로 비동기 캡처 시작
                _acquisition = cam.StartFrameAcquisition();
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
                SafeStopAcquisition_NoLock();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// _gate 안에서만 호출 (락 밖에서 호출하지 말 것)
        /// </summary>
        private void SafeStopAcquisition_NoLock()
        {
            if (_acquisition is not null)
            {
                try
                {
                    _acquisition.Dispose(); // IAcquisition.Dispose() 가 AcquisitionStop + 큐 flush 수행
                }
                catch
                {
                    // 카메라 핸들이 이미 죽었거나 BadHandle 등인 경우는 무시
                }
                finally
                {
                    _acquisition = null;
                }
            }

            if (_openCamera is not null)
            {
                try
                {
                    _openCamera.FrameReceived -= OnFrameReceived;
                }
                catch
                {
                    // 여러 번 detach 시도해도 문제 없게
                }
            }
        }

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

                byte[] buffer = ArrayPool<byte>.Shared.Rent(imageSize);
                try
                {
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
                        bitmap.Dispose();
                        return;
                    }

                    FrameReady?.Invoke(this, bitmap);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch
            {
                // 이벤트 핸들러에서 예외 전파 금지 (Vmb 내부 스레드 보호)
            }
        }

        public async Task<Bitmap> CaptureAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();

                var cam = EnsureOpenCamera();

                using IFrame frame = cam.AcquireSingleImage(TimeSpan.FromMilliseconds(500));

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

                byte[] buffer = ArrayPool<byte>.Shared.Rent(imageSize);
                try
                {
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
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> GetExposureTimeAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();
                double value = cam.Features.ExposureTime;
                return value;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> SetExposureTimeAsync(double exposureTime, CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();

                cam.Features.ExposureTime = exposureTime;
                double applied = cam.Features.ExposureTime;
                return applied;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> GetGainAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();
                double value = cam.Features.Gain;
                return value;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> SetGainAsync(double gain, CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();
                cam.Features.Gain = gain;
                double applied = cam.Features.Gain;
                return applied;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> GetGammaAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();
                double value = cam.Features.Gamma;
                return value;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<double> SetGammaAsync(double gamma, CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ct.ThrowIfCancellationRequested();
                var cam = EnsureOpenCamera();
                cam.Features.Gamma = gamma;
                double applied = cam.Features.Gamma;
                return applied;
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
                SafeStopAcquisition_NoLock();

                if (_openCamera is not null)
                {
                    try
                    {
                        _openCamera.FrameReceived -= OnFrameReceived;
                    }
                    catch
                    {
                    }

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
