using AvaloniaApp.Core.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Infrastructure
{
    public sealed class VimbaCameraService : IAsyncDisposable
    {
        private readonly IVmbSystem _system;
        private readonly bool _ownsSystem;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IOpenCamera? _openCamera;
        private IAcquisition? _acquisition;
        private bool _disposed;

        private long _generation;
        private long _activeGeneration;
        private bool _frameHandlerAttached;

        public event Action<bool>? StreamingStateChanged;

        private readonly Channel<FrameData> _frames = Channel.CreateBounded<FrameData>(
            new BoundedChannelOptions(1)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelReader<FrameData> Frames => _frames.Reader;
        public CameraData? ConnectedCameraInfo { get; private set; }
        public bool IsStreaming => _acquisition is not null;

        public VimbaCameraService() : this(IVmbSystem.Startup(), ownsSystem: true) { }

        internal VimbaCameraService(IVmbSystem system, bool ownsSystem)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
            _ownsSystem = ownsSystem;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VimbaCameraService));
        }

        private IOpenCamera EnsureOpenCamera() => _openCamera ?? throw new InvalidOperationException("카메라가 연결되지 않았습니다.");

        private void InvalidatePreview()
        {
            Volatile.Write(ref _activeGeneration, 0);
            Interlocked.Increment(ref _generation);
            while (_frames.Reader.TryRead(out var old)) old.Dispose();
        }

        private void AttachFrameHandler()
        {
            if (_openCamera is null || _frameHandlerAttached) return;
            _openCamera.FrameReceived += OnFrameReceived;
            _frameHandlerAttached = true;
        }

        private void DetachFrameHandler()
        {
            if (_openCamera is null || !_frameHandlerAttached) return;
            try { _openCamera.FrameReceived -= OnFrameReceived; } catch { }
            _frameHandlerAttached = false;
        }

        private void SafeStopAcquisition()
        {
            InvalidatePreview();

            // [수정] StopFrameAcquisition 호출 제거 (IAcquisition Dispose로 충분함)
            if (_acquisition is not null)
            {
                try { _acquisition.Dispose(); }
                catch { }
                finally { _acquisition = null; }
            }

            DetachFrameHandler();

            // 상태 변경 알림
            StreamingStateChanged?.Invoke(false);
        }

        public Task<IReadOnlyList<CameraData>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var result = _system.GetCameras()
                .Where(c => !IsIgnoredModel(c.ModelName) && !IsIgnoredModel(c.Name))
                .Select(c => new CameraData(c.Id, c.Name, c.Serial, c.ModelName))
                .ToArray();

            return Task.FromResult<IReadOnlyList<CameraData>>(result);
        }

        private bool IsIgnoredModel(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var upper = name.ToUpperInvariant();
            return upper.Contains("SIMULATOR") ||
                   upper.Contains("VIMBA X") ||
                   upper.Contains("DEFECT") ||
                   upper.Contains("VIRTUAL");
        }

        public async Task StartPreviewAsync(CancellationToken ct, string cameraId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(cameraId)) throw new ArgumentNullException(nameof(cameraId));

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_openCamera is not null && _acquisition is not null && ConnectedCameraInfo?.Id == cameraId)
                    return;

                if (_openCamera is not null)
                {
                    SafeStopAcquisition();
                    try { _openCamera.Dispose(); } catch { }
                    _openCamera = null;
                    ConnectedCameraInfo = null;
                }

                const int MaxRetries = 3;
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        var camera = _system.GetCameraByID(cameraId)
                                     ?? throw new InvalidOperationException($"Camera '{cameraId}' not found.");

                        _openCamera = camera.Open();
                        ConnectedCameraInfo = new CameraData(camera.Id, camera.Name, camera.Serial, camera.ModelName);

                        var gen = Interlocked.Increment(ref _generation);
                        Volatile.Write(ref _activeGeneration, gen);

                        AttachFrameHandler();
                        _acquisition = _openCamera.StartFrameAcquisition();

                        StreamingStateChanged?.Invoke(true);

                        Debug.WriteLine($"[Vimba] Camera Started: {cameraId}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Vimba] Start failed ({attempt}): {ex.Message}");
                        SafeStopAcquisition();
                        if (_openCamera != null)
                        {
                            try { _openCamera.Dispose(); } catch { }
                            _openCamera = null;
                            ConnectedCameraInfo = null;
                        }

                        if (attempt == MaxRetries) throw;
                        await Task.Delay(300, ct).ConfigureAwait(false);
                    }
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
                SafeStopAcquisition();

                if (_openCamera is not null)
                {
                    try { _openCamera.Dispose(); } catch { }
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
                if (gen == 0) return;

                if (frame.FrameStatus != IFrame.FrameStatusValue.Completed ||
                    frame.PayloadType != IFrame.PayloadTypeValue.Image ||
                    frame.PixelFormat != IFrame.PixelFormatValue.Mono8)
                    return;

                int width = checked((int)frame.Width);
                int height = checked((int)frame.Height);
                if (width <= 0 || height <= 0) return;

                int packedStride = width;
                int packedLength = checked(packedStride * height);

                if (frame.ImageData == IntPtr.Zero || frame.BufferSize < (uint)packedLength) return;

                int srcStride = packedStride;
                if (height > 0 && (frame.BufferSize % (uint)height) == 0)
                {
                    uint pitch = frame.BufferSize / (uint)height;
                    if (pitch >= (uint)packedStride) srcStride = (int)pitch;
                }

                if (Volatile.Read(ref _activeGeneration) != gen) return;

                var buffer = ArrayPool<byte>.Shared.Rent(packedLength);
                try
                {
                    byte* src = (byte*)frame.ImageData;
                    fixed (byte* dst0 = buffer)
                    {
                        if (srcStride == packedStride)
                            Buffer.MemoryCopy(src, dst0, packedLength, packedLength);
                        else
                        {
                            for (int y = 0; y < height; y++)
                                Buffer.MemoryCopy(src + (long)y * srcStride, dst0 + (long)y * packedStride, packedStride, packedStride);
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
                    if (_frames.Reader.TryRead(out var old)) old.Dispose();
                    if (!_frames.Writer.TryWrite(packet)) packet.Dispose();
                }
            }
            catch { }
        }

        public async Task<double> GetExposureTimeAsync(CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { return EnsureOpenCamera().Features.ExposureTime; } finally { _gate.Release(); } }
        public async Task<double> SetExposureTimeAsync(double val, CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { var c = EnsureOpenCamera(); c.Features.ExposureTime = val; return c.Features.ExposureTime; } finally { _gate.Release(); } }

        public async Task<double> GetGainAsync(CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { return EnsureOpenCamera().Features.Gain; } finally { _gate.Release(); } }
        public async Task<double> SetGainAsync(double val, CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { var c = EnsureOpenCamera(); c.Features.Gain = val; return c.Features.Gain; } finally { _gate.Release(); } }

        public async Task<double> GetGammaAsync(CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { return EnsureOpenCamera().Features.Gamma; } finally { _gate.Release(); } }
        public async Task<double> SetGammaAsync(double val, CancellationToken ct) { ThrowIfDisposed(); await _gate.WaitAsync(ct); try { var c = EnsureOpenCamera(); c.Features.Gamma = val; return c.Features.Gamma; } finally { _gate.Release(); } }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                SafeStopAcquisition();
                if (_openCamera is not null)
                {
                    try { _openCamera.Dispose(); } catch { }
                    _openCamera = null;
                }
                if (_ownsSystem) try { _system.Shutdown(); } catch { }
                _frames.Writer.TryComplete();
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}