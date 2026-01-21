using AvaloniaApp.Core.Enums;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AvaloniaApp.Infrastructure.Service
{
    public sealed class FakeCameraService : ICameraService
    {
        private const int SensorWidth = 5328;
        private const int SensorHeight = 3040;

        private readonly string _imageDirectory;
        private readonly int _frameRateFps;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private List<byte[]> _sourceImages = new();
        private int _currentImageIndex = 0;
        private int _imageWidth;
        private int _imageHeight;
        private bool _disposed;

        private volatile bool _isIntentionallyStreaming;
        private Task? _streamingTask;
        private CancellationTokenSource? _streamingCts;

        public event Action<bool>? StreamingStateChanged;
        public event Action<CameraConnectionState>? ConnectionStateChanged;
        public event Action<string>? ErrorOccurred;

        private readonly Channel<FrameData> _frames = Channel.CreateBounded<FrameData>(
            new BoundedChannelOptions(1)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelReader<FrameData> Frames => _frames.Reader;
        public CameraData? ConnectedCameraInfo { get; private set; }
        public bool IsStreaming => _isIntentionallyStreaming;

        public FakeCameraService(string imageDirectory, int frameRateFps = 10)
        {
            _imageDirectory = imageDirectory ?? throw new ArgumentNullException(nameof(imageDirectory));
            _frameRateFps = frameRateFps > 0 ? frameRateFps : 10;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FakeCameraService));
        }

        public Task<IReadOnlyList<CameraData>> GetCameraListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var result = new List<CameraData>
            {
                new CameraData("fake-camera-001", "Fake Camera", "FAKE001", "FakeCamera Simulator")
            };

            return Task.FromResult<IReadOnlyList<CameraData>>(result);
        }

        public async Task StartPreviewAsync(CancellationToken ct, string cameraId)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_isIntentionallyStreaming) return;

                _isIntentionallyStreaming = true;
                ConnectedCameraInfo = new CameraData(cameraId, "Fake Camera", "FAKE001", "FakeCamera Simulator");

                ConnectionStateChanged?.Invoke(CameraConnectionState.Connecting);

                if (!LoadSourceImages())
                {
                    _isIntentionallyStreaming = false;
                    ConnectedCameraInfo = null;
                    ConnectionStateChanged?.Invoke(CameraConnectionState.Error);
                    ErrorOccurred?.Invoke($"Failed to load images from: {_imageDirectory}");
                    return;
                }

                _streamingCts = new CancellationTokenSource();
                _streamingTask = Task.Run(() => StreamingLoopAsync(_streamingCts.Token));

                ConnectionStateChanged?.Invoke(CameraConnectionState.Streaming);
                StreamingStateChanged?.Invoke(true);
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
                _isIntentionallyStreaming = false;
                _streamingCts?.Cancel();

                if (_streamingTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_streamingTask, Task.Delay(2000, ct));
                    }
                    catch { }
                }

                _streamingCts = null;
                _streamingTask = null;

                while (_frames.Reader.TryRead(out var frame)) frame.Dispose();

                StreamingStateChanged?.Invoke(false);
                ConnectionStateChanged?.Invoke(CameraConnectionState.Disconnected);
            }
            finally
            {
                _gate.Release();
            }
        }

        private bool LoadSourceImages()
        {
            try
            {
                if (!Directory.Exists(_imageDirectory))
                {
                    Debug.WriteLine($"[FakeCameraService] Directory not found: {_imageDirectory}");
                    return false;
                }

                var extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" };
                var files = extensions
                    .SelectMany(ext => Directory.GetFiles(_imageDirectory, ext))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    Debug.WriteLine($"[FakeCameraService] No image files found in: {_imageDirectory}");
                    return false;
                }

                _sourceImages.Clear();
                _currentImageIndex = 0;

                foreach (var file in files)
                {
                    byte[]? imageData = LoadAndProcessImage(file);
                    if (imageData != null)
                    {
                        _sourceImages.Add(imageData);
                    }
                }

                Debug.WriteLine($"[FakeCameraService] Loaded {_sourceImages.Count} images from: {_imageDirectory}");
                return _sourceImages.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FakeCameraService] Error loading images: {ex.Message}");
                return false;
            }
        }

        private byte[]? LoadAndProcessImage(string filePath)
        {
            try
            {
                using var srcMat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                if (srcMat.Empty())
                {
                    Debug.WriteLine($"[FakeCameraService] Failed to load image: {filePath}");
                    return null;
                }

                // Convert to grayscale if needed
                Mat grayMat;
                if (srcMat.Channels() > 1)
                {
                    grayMat = new Mat();
                    Cv2.CvtColor(srcMat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    grayMat = srcMat.Clone();
                }

                // Resize/pad to match sensor size
                Mat finalMat = ResizeToSensorSize(grayMat);
                grayMat.Dispose();

                _imageWidth = finalMat.Cols;
                _imageHeight = finalMat.Rows;
                int length = _imageWidth * _imageHeight;

                byte[] imageData = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(finalMat.Data, imageData, 0, length);

                finalMat.Dispose();
                return imageData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FakeCameraService] Error loading image {filePath}: {ex.Message}");
                return null;
            }
        }

        private Mat ResizeToSensorSize(Mat source)
        {
            if (source.Cols == SensorWidth && source.Rows == SensorHeight)
            {
                return source.Clone();
            }

            // Calculate scaling to fit within sensor while maintaining aspect ratio
            double scaleX = (double)SensorWidth / source.Cols;
            double scaleY = (double)SensorHeight / source.Rows;
            double scale = Math.Min(scaleX, scaleY);

            int newWidth = (int)(source.Cols * scale);
            int newHeight = (int)(source.Rows * scale);

            // Resize the image
            Mat resized = new Mat();
            Cv2.Resize(source, resized, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

            // Create canvas at sensor size (black background)
            Mat canvas = new Mat(SensorHeight, SensorWidth, MatType.CV_8UC1, Scalar.Black);

            // Calculate offset to center the image
            int offsetX = (SensorWidth - newWidth) / 2;
            int offsetY = (SensorHeight - newHeight) / 2;

            // Copy resized image to center of canvas
            var roi = new Rect(offsetX, offsetY, newWidth, newHeight);
            resized.CopyTo(canvas[roi]);

            resized.Dispose();
            return canvas;
        }

        private async Task StreamingLoopAsync(CancellationToken ct)
        {
            int delayMs = 1000 / _frameRateFps;

            Debug.WriteLine($"[FakeCameraService] Streaming started at {_frameRateFps} FPS");

            while (!ct.IsCancellationRequested && _isIntentionallyStreaming)
            {
                try
                {
                    var frame = CreateFrameFromSource();
                    if (frame != null)
                    {
                        if (!_frames.Writer.TryWrite(frame))
                        {
                            if (_frames.Reader.TryRead(out var oldFrame))
                            {
                                oldFrame.Dispose();
                            }

                            if (!_frames.Writer.TryWrite(frame))
                            {
                                frame.Dispose();
                            }
                        }
                    }

                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FakeCameraService] Streaming error: {ex.Message}");
                }
            }

            Debug.WriteLine("[FakeCameraService] Streaming stopped");
        }

        private FrameData? CreateFrameFromSource()
        {
            if (_sourceImages.Count == 0) return null;

            byte[] currentImage = _sourceImages[_currentImageIndex];
            _currentImageIndex = (_currentImageIndex + 1) % _sourceImages.Count;

            int length = _imageWidth * _imageHeight;
            var buffer = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                Buffer.BlockCopy(currentImage, 0, buffer, 0, length);
                return FrameData.Wrap(buffer, _imageWidth, _imageHeight, _imageWidth, length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return null;
            }
        }

        // Parameter methods - return default values for fake camera
        public Task<double> GetExposureTimeAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(100000.0); // 100ms in microseconds
        }

        public Task<double> SetExposureTimeAsync(double val, CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(val); // Just return the requested value
        }

        public Task<double> GetGainAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(0.0);
        }

        public Task<double> SetGainAsync(double val, CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(val);
        }

        public Task<double> GetGammaAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(1.0);
        }

        public Task<double> SetGammaAsync(double val, CancellationToken ct)
        {
            ThrowIfDisposed();
            return Task.FromResult(val);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _isIntentionallyStreaming = false;
            _streamingCts?.Cancel();

            bool lockTaken = await _gate.WaitAsync(2000).ConfigureAwait(false);

            try
            {
                if (_streamingTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_streamingTask, Task.Delay(1000));
                    }
                    catch { }
                }

                _frames.Writer.TryComplete();
                while (_frames.Reader.TryRead(out var frame)) frame.Dispose();

                _sourceImages.Clear();
            }
            finally
            {
                if (lockTaken) _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
