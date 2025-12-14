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
    /// <summary>
    /// Vimba X / VmbNET 카메라 서비스.
    ///
    /// 권장 사용 플로우:
    /// - Start: GetCameraListAsync() -> StartPreviewAsync(ct, id)
    /// - Stop : StopPreviewAndDisconnectAsync(ct)
    ///
    /// FrameReady로 전달되는 Bitmap 소유권은 구독자에게 있음(구독자가 Dispose 책임).
    /// </summary>
    public sealed class VimbaCameraService : IAsyncDisposable
    {
        private readonly IVmbSystem _system;
        private readonly bool _ownsSystem;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IOpenCamera? _openCamera;
        private IAcquisition? _acquisition;
        private bool _disposed;

        // 프리뷰 종료 직후 늦게 도착하는 프레임 무시용
        private long _generation;
        private long _activeGeneration; // 0이면 비활성
        private bool _frameHandlerAttached;

        public CameraInfo? ConnectedCameraInfo { get; private set; }

        /// <summary>
        /// 연속 프리뷰용 프레임 이벤트.
        /// Bitmap 소유권은 구독자에게 넘어감 (구독자가 Dispose 책임).
        /// </summary>
        public event EventHandler<Bitmap>? FrameReady;

        public bool IsStreaming => _acquisition is not null;

        public VimbaCameraService()
            : this(IVmbSystem.Startup(), ownsSystem: true)
        {
        }

        /// <summary>
        /// 테스트를 위한 주입용 생성자(하드웨어 없이 Fake를 넣거나, System 라이프사이클을 외부가 관리할 때).
        /// </summary>
        internal VimbaCameraService(IVmbSystem system, bool ownsSystem)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
            _ownsSystem = ownsSystem;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VimbaCameraService));
        }

        private IOpenCamera EnsureOpenCamera()
            => _openCamera ?? throw new InvalidOperationException("카메라가 연결되지 않았습니다.");

        private void InvalidatePreview_NoLock()
        {
            Volatile.Write(ref _activeGeneration, 0);
            Interlocked.Increment(ref _generation);
        }

        private void AttachFrameHandler_NoLock()
        {
            if (_openCamera is null) return;
            if (_frameHandlerAttached) return;

            _openCamera.FrameReceived += OnFrameReceived;
            _frameHandlerAttached = true;
        }

        private void DetachFrameHandler_NoLock()
        {
            if (_openCamera is null) return;
            if (!_frameHandlerAttached) return;

            try { _openCamera.FrameReceived -= OnFrameReceived; }
            catch { /* 여러 번 detach 시도 등 방어 */ }

            _frameHandlerAttached = false;
        }

        /// <summary>
        /// _gate 안에서만 호출 (락 밖에서 호출하지 말 것)
        /// </summary>
        private void SafeStopAcquisition_NoLock()
        {
            // 먼저 프리뷰 무효화(늦게 도착한 프레임 폐기)
            InvalidatePreview_NoLock();

            if (_acquisition is not null)
            {
                try
                {
                    _acquisition.Dispose(); // Stop + flush 역할
                }
                catch
                {
                    // 카메라 핸들이 이미 죽었거나 BadHandle 등인 경우 무시
                }
                finally
                {
                    _acquisition = null;
                }
            }

            // 이벤트도 같이 끊어두는 게 안전(Stop 직후 콜백 최소화)
            DetachFrameHandler_NoLock();
        }

        // -------------------------
        // Public API
        // -------------------------

        public Task<IReadOnlyList<PixelFormatInfo>> GetSupportPixelformatListAsync(CancellationToken ct, string? id)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            _ = _system.GetCameraByID(id)
                ?? throw new InvalidOperationException($"Camera '{id}' not found.");

            // 현재는 Mono8만 사용(확장 여지)
            IReadOnlyList<PixelFormatInfo> list = new[]
            {
                new PixelFormatInfo(name: "Mono8", displayName: "Mono8", isAvailable: true)
            };

            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<CameraInfo>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var cameras = _system.GetCameras();

            var result = cameras
                .Select(c => new CameraInfo(
                    id: c.Id,
                    name: c.Name,
                    serial: c.Serial,
                    modelName: c.ModelName))
                .ToArray();

            return Task.FromResult<IReadOnlyList<CameraInfo>>(result);
        }

        /// <summary>
        /// 권장: Start = Connect + StartStream을 한 번에 보장.
        /// </summary>
        public async Task StartPreviewAsync(CancellationToken ct, string cameraId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(cameraId))
                throw new ArgumentNullException(nameof(cameraId));

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                // (1) 필요하면 연결
                if (_openCamera is null || ConnectedCameraInfo?.Id != cameraId)
                {
                    // 기존 자원 정리
                    SafeStopAcquisition_NoLock();

                    if (_openCamera is not null)
                    {
                        DetachFrameHandler_NoLock();
                        _openCamera.Dispose();
                        _openCamera = null;
                    }

                    ConnectedCameraInfo = null;

                    var camera = _system.GetCameraByID(cameraId)
                        ?? throw new InvalidOperationException($"Camera '{cameraId}' not found.");

                    _openCamera = camera.Open();

                    ConnectedCameraInfo = new CameraInfo(
                        id: camera.Id,
                        name: camera.Name,
                        serial: camera.Serial,
                        modelName: camera.ModelName);
                }

                // (2) 스트리밍 시작(이미면 no-op)
                if (_acquisition is not null)
                    return;

                var cam = EnsureOpenCamera();

                // 프리뷰 활성 세대 설정
                var gen = Interlocked.Increment(ref _generation);
                Volatile.Write(ref _activeGeneration, gen);

                AttachFrameHandler_NoLock();
                try
                {
                    _acquisition = cam.StartFrameAcquisition();
                }
                catch
                {
                    // Start 실패 시 롤백
                    InvalidatePreview_NoLock();
                    SafeStopAcquisition_NoLock();
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 권장: Stop = StopStream + Disconnect를 한 번에 보장.
        /// </summary>
        public async Task StopPreviewAndDisconnectAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                // Stop preview
                SafeStopAcquisition_NoLock();

                // Disconnect
                if (_openCamera is not null)
                {
                    DetachFrameHandler_NoLock();
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

        // -------------------------
        // Legacy API (원하면 유지)
        // -------------------------

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

                SafeStopAcquisition_NoLock();

                if (_openCamera is not null)
                {
                    DetachFrameHandler_NoLock();
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

                SafeStopAcquisition_NoLock();

                if (_openCamera is not null)
                {
                    DetachFrameHandler_NoLock();
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

                if (_acquisition is not null)
                    return;

                var cam = EnsureOpenCamera();

                var gen = Interlocked.Increment(ref _generation);
                Volatile.Write(ref _activeGeneration, gen);

                AttachFrameHandler_NoLock();
                try
                {
                    _acquisition = cam.StartFrameAcquisition();
                }
                catch
                {
                    InvalidatePreview_NoLock();
                    SafeStopAcquisition_NoLock();
                    throw;
                }
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

        // -------------------------
        // Frame callback
        // -------------------------

        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            var handler = FrameReady;
            if (handler is null)
                return;

            // 프리뷰 비활성이면 즉시 무시
            if (Volatile.Read(ref _activeGeneration) == 0)
                return;

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

                    // Stop/Disconnect 직후 늦게 만든 프레임이면 폐기
                    if (Volatile.Read(ref _activeGeneration) == 0)
                    {
                        bitmap.Dispose();
                        return;
                    }

                    handler.Invoke(this, bitmap); // 소유권: 구독자
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch
            {
                // 콜백 예외 전파 금지
            }
        }

        // -------------------------
        // Still image capture
        // -------------------------

        public async Task<Bitmap> CaptureAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                // 정책: 프리뷰 중 캡처 금지(단순/안전)
                if (_acquisition is not null)
                    throw new InvalidOperationException("스트리밍 중에는 CaptureAsync를 호출할 수 없습니다. (StopPreview 후 캡처)");

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
                if (frame.BufferSize < (uint)imageSize)
                    throw new InvalidOperationException($"버퍼 크기 부족: BufferSize={frame.BufferSize}, 필요={imageSize}");

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

        // -------------------------
        // Feature getters/setters
        // -------------------------

        public async Task<double> GetExposureTimeAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                return EnsureOpenCamera().Features.ExposureTime;
            }
            finally { _gate.Release(); }
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
                return cam.Features.ExposureTime;
            }
            finally { _gate.Release(); }
        }

        public async Task<double> GetGainAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                return EnsureOpenCamera().Features.Gain;
            }
            finally { _gate.Release(); }
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
                return cam.Features.Gain;
            }
            finally { _gate.Release(); }
        }

        public async Task<double> GetGammaAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                return EnsureOpenCamera().Features.Gamma;
            }
            finally { _gate.Release(); }
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
                return cam.Features.Gamma;
            }
            finally { _gate.Release(); }
        }

        // -------------------------
        // Dispose
        // -------------------------

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
                    DetachFrameHandler_NoLock();
                    _openCamera.Dispose();
                    _openCamera = null;
                }

                ConnectedCameraInfo = null;

                if (_ownsSystem)
                {
                    try { _system.Shutdown(); }
                    catch { }
                }
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
