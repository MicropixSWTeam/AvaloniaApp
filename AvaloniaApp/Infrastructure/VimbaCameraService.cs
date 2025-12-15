using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    /// <summary>
    /// Vimba X / VmbNET 카메라 서비스.
    /// - Frames 채널로 FramePacket(Gray8) 전달
    /// - Packet은 ArrayPool 버퍼 소유 → 소비자가 반드시 Dispose해야 함
    /// </summary>
    public sealed class VimbaCameraService : IAsyncDisposable
    {
        private readonly IVmbSystem _system;
        private readonly bool _ownsSystem;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IOpenCamera? _openCamera;
        private IAcquisition? _acquisition;
        private bool _disposed;

        private long _generation;
        private long _activeGeneration; // 0이면 비활성
        private bool _frameHandlerAttached;

        // 최신 1프레임만 유지. (드롭 시 Dispose를 우리가 직접 해야 해서 TryWrite + TryRead 방식)
        private readonly Channel<FrameData> _frames = Channel.CreateBounded<FrameData>(
            new BoundedChannelOptions(1)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelReader<FrameData> Frames => _frames.Reader;
        public CameraInfo? ConnectedCameraInfo { get; private set; }
        public bool IsStreaming => _acquisition is not null;

        public VimbaCameraService() : this(IVmbSystem.Startup(), ownsSystem: true) { }

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
        private IOpenCamera EnsureOpenCamera() => _openCamera ?? throw new InvalidOperationException("카메라가 연결되지 않았습니다.");
        private void InvalidatePreview()
        {
            Volatile.Write(ref _activeGeneration, 0);
            Interlocked.Increment(ref _generation);

            // 남아있는 Packet 정리(버퍼 반환)
            while (_frames.Reader.TryRead(out var old))
                old.Dispose();
        }
        private void AttachFrameHandler()
        {
            if (_openCamera is null) return;
            if (_frameHandlerAttached) return;

            _openCamera.FrameReceived += OnFrameReceived;
            _frameHandlerAttached = true;
        }
        private void DetachFrameHandler()
        {
            if (_openCamera is null) return;
            if (!_frameHandlerAttached) return;

            try { _openCamera.FrameReceived -= OnFrameReceived; }
            catch { }

            _frameHandlerAttached = false;
        }
        private void SafeStopAcquisition()
        {
            InvalidatePreview();

            if (_acquisition is not null)
            {
                try { _acquisition.Dispose(); }
                catch { }
                finally { _acquisition = null; }
            }

            DetachFrameHandler();
        }
        public Task<IReadOnlyList<CameraInfo>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var result = _system.GetCameras()
                .Select(c => new CameraInfo(c.Id, c.Name, c.Serial, c.ModelName))
                .ToArray();

            return Task.FromResult<IReadOnlyList<CameraInfo>>(result);
        }
        public async Task StartPreviewAsync(CancellationToken ct, string cameraId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(cameraId))
                throw new ArgumentNullException(nameof(cameraId));

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                if (_openCamera is null || ConnectedCameraInfo?.Id != cameraId)
                {
                    SafeStopAcquisition();

                    if (_openCamera is not null)
                    {
                        DetachFrameHandler();
                        _openCamera.Dispose();
                        _openCamera = null;
                    }

                    ConnectedCameraInfo = null;

                    var camera = _system.GetCameraByID(cameraId)
                        ?? throw new InvalidOperationException($"Camera '{cameraId}' not found.");

                    _openCamera = camera.Open();
                    ConnectedCameraInfo = new CameraInfo(camera.Id, camera.Name, camera.Serial, camera.ModelName);
                }

                if (_acquisition is not null)
                    return;

                var cam = EnsureOpenCamera();

                var gen = Interlocked.Increment(ref _generation);
                Volatile.Write(ref _activeGeneration, gen);

                AttachFrameHandler();

                try
                {
                    _acquisition = cam.StartFrameAcquisition();
                }
                catch
                {
                    InvalidatePreview();
                    SafeStopAcquisition();
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        public async Task StopPreviewAndDisconnectAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                SafeStopAcquisition();

                if (_openCamera is not null)
                {
                    DetachFrameHandler();
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
        private unsafe void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            try
            {
                using var frame = e.Frame;

                var gen = Volatile.Read(ref _activeGeneration);
                if (gen == 0)
                    return;

                if (frame.FrameStatus != IFrame.FrameStatusValue.Completed)
                    return;
                if (frame.PayloadType != IFrame.PayloadTypeValue.Image)
                    return;
                if (frame.PixelFormat != IFrame.PixelFormatValue.Mono8)
                    return;

                int width = checked((int)frame.Width);
                int height = checked((int)frame.Height);

                if (width <= 0 || height <= 0)
                    return;

                int packedStride = width;
                int packedLength = checked(packedStride * height);

                if (frame.ImageData == IntPtr.Zero)
                    return;
                if (frame.BufferSize < (uint)packedLength)
                    return;

                int srcStride = packedStride;
                uint bufSize = frame.BufferSize;
                if (height > 0 && (bufSize % (uint)height) == 0)
                {
                    uint pitch = bufSize / (uint)height;
                    if (pitch >= (uint)packedStride && pitch <= int.MaxValue)
                        srcStride = (int)pitch;
                }

                if (Volatile.Read(ref _activeGeneration) != gen)
                    return;

                var buffer = ArrayPool<byte>.Shared.Rent(packedLength);

                try
                {
                    byte* src = (byte*)frame.ImageData;
                    fixed (byte* dst0 = buffer)
                    {
                        byte* dst = dst0;

                        if (srcStride == packedStride)
                        {
                            Buffer.MemoryCopy(src, dst, packedLength, packedLength);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + (long)y * srcStride,
                                    dst + (long)y * packedStride,
                                    packedStride,
                                    packedStride);
                            }
                        }
                    }
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                if (Volatile.Read(ref _activeGeneration) != gen)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                var packet = FrameData.Wrap(buffer, width, height, packedStride, packedLength);

                if (!_frames.Writer.TryWrite(packet))
                {
                    if (_frames.Reader.TryRead(out var old))
                        old.Dispose();

                    if (!_frames.Writer.TryWrite(packet))
                        packet.Dispose();
                }
            }
            catch
            {
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
                SafeStopAcquisition();

                if (_openCamera is not null)
                {
                    DetachFrameHandler();
                    _openCamera.Dispose();
                    _openCamera = null;
                }

                ConnectedCameraInfo = null;

                if (_ownsSystem)
                {
                    try { _system.Shutdown(); }
                    catch { }
                }

                try { _frames.Writer.TryComplete(); }
                catch { }
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}