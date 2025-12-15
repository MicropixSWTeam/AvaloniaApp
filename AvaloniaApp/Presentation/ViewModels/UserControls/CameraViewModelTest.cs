using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
using AvaloniaApp.Presentation.Services;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModelTest : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        private readonly ImageConverter _imageConverter;

        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;

        private WriteableBitmap? _previewFullImage;
        private WriteableBitmap? _previewRectImage;

        private int _uiScheduled;              
    
        private int _previewActive;
        private FrameData? _previewFullFrameData;
        private FrameData? _previewRectFrameData;

        private int _stopCaptureRequested;  
        private FrameData? _captureFrameData;

        private long _lastRenderTs;
        private double _fpsEma;

        public event Action? PreviewInvalidated;

        [ObservableProperty] private ObservableCollection<CameraInfo> cameras = new();
        [ObservableProperty] private CameraInfo? selectedCamera;

        [ObservableProperty] private Bitmap? previewBitmap;
        [ObservableProperty] private bool isPreviewing;

        [ObservableProperty] private double previewFps;

        public CameraViewModelTest(VimbaCameraService camera, UiDispatcher ui, OperationRunner runner)
            : base(ui, runner)
        {
            _cameraService = camera ?? throw new ArgumentNullException(nameof(camera));
            _imageConverter = new ImageConverter();
        }

        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync(
                key: "camera.refresh",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 목록 불러오는 중...");
                    var list = await _cameraService.GetCameraListAsync(ct).ConfigureAwait(false);

                    await UiInvokeAsync(() =>
                    {
                        Cameras.Clear();
                        foreach (var c in list) Cameras.Add(c);
                        SelectedCamera ??= Cameras.FirstOrDefault();
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "GetCameraList";
                    opt.Timeout = TimeSpan.FromSeconds(3);
                });
        }
        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            await RefreshCamerasAsync();
            var cam = SelectedCamera ?? throw new InvalidOperationException("카메라를 선택하세요.");

            await RunOperationAsync(
                key: "PreviewStart",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("프리뷰 시작 중...");

                    await _cameraService.StartPreviewAsync(ct, cam.Id).ConfigureAwait(false);

                    // 소비 루프는 1개만
                    StartConsumeLoop();

                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = true;
                        Volatile.Write(ref _previewActive, 1);
                        PreviewFps = 0;
                        _fpsEma = 0;
                        _lastRenderTs = 0;
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "StartPreview";
                    opt.Timeout = TimeSpan.FromSeconds(5);
                });
        }
        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            await RunOperationAsync(
                key: "PreviewStop",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("프리뷰 정지 중...");

                    // UI 업데이트/렌더 중단 플래그를 먼저 내림
                    Volatile.Write(ref _previewActive, 0);

                    StopConsumeLoop();

                    await AwaitConsumeLoopAsync().ConfigureAwait(false);

                    await _cameraService.StopPreviewAndDisconnectAsync(ct).ConfigureAwait(false);

                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = false;
                        PreviewFps = 0;

                        Interlocked.Exchange(ref _uiScheduled, 0);
                        Interlocked.Exchange(ref _previewFullFrameData, null)?.Dispose();
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "StopPreview";
                    opt.Timeout = TimeSpan.FromSeconds(5);
                });
        }
        private void StartConsumeLoop()
        {
            if (_consumeTask is not null && !_consumeTask.IsCompleted)
                return;

            _consumeCts?.Dispose();
            _consumeCts = new CancellationTokenSource();
            _consumeTask = ConsumeFramesAsync(_consumeCts.Token);
        }
        private void StopConsumeLoop()
        {
            try { _consumeCts?.Cancel(); } catch { }
        }
        private async Task AwaitConsumeLoopAsync()
        {
            var t = _consumeTask;
            if (t is null) return;

            try { await t.ConfigureAwait(false); }
            catch { }
            finally
            {
                _consumeTask = null;
                _consumeCts?.Dispose();
                _consumeCts = null;

                Interlocked.Exchange(ref _previewFullFrameData, null)?.Dispose();
                Interlocked.Exchange(ref _uiScheduled, 0);
            }
        }
        private async Task ConsumeFramesAsync(CancellationToken ct)
        {
            var reader = _cameraService.Frames;

            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var frame))
                    {
                        // 최신 1개만 유지
                        var old = Interlocked.Exchange(ref _previewFullFrameData, frame);
                        old?.Dispose();

                        // UI 작업은 동시에 1개만 예약 (coalescing)
                        if (Interlocked.Exchange(ref _uiScheduled, 1) == 0)
                        {
                            // Avalonia Dispatcher에 UI 갱신 요청
                            _ui.Post(RenderPendingOnUi);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* log */ }
        }
        public void RequestStopCapture() => Interlocked.Exchange(ref _stopCaptureRequested, 1);
        private void RenderPendingOnUi()
        {
            Interlocked.Exchange(ref _uiScheduled, 0);
            var packet = Interlocked.Exchange(ref _previewFullFrameData, null);
            if (packet is null) return;

            try
            {
                if (Volatile.Read(ref _previewActive) == 0)
                    return;

                EnsureSharedPreview(packet.Width, packet.Height);

                if (_previewFullImage is null) return;
                _imageConverter.ConvertFrameDataToWriteableBitmap(_previewFullImage, packet);

                if (Interlocked.Exchange(ref _stopCaptureRequested, 0) == 1)
                {
                    _captureFrameData?.Dispose();
                    _captureFrameData = FrameData.Clone(packet); // 독립 데이터
                }

                UpdatePreviewFpsOnUi();
                PreviewInvalidated?.Invoke();
            }
            finally
            {
                packet.Dispose();
            }


            if (Volatile.Read(ref _previewFullFrameData) is not null && Interlocked.Exchange(ref _uiScheduled, 1) == 0)
                _ui.Post(RenderPendingOnUi);
        }
        private void EnsureSharedPreview(int width, int height)
        {
            if (_previewFullImage is not null)
            {
                var ps = _previewFullImage.PixelSize;
                if (ps.Width == width && ps.Height == height)
                    return;

                _previewFullImage.Dispose();
                _previewFullImage = null;
            }

            _previewFullImage = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

            // 인스턴스 교체는 사이즈 변경 때만
            PreviewBitmap = _previewFullImage;
        }
        private void UpdatePreviewFpsOnUi()
        {
            long now = Stopwatch.GetTimestamp();
            long last = _lastRenderTs;
            _lastRenderTs = now;

            if (last == 0)
                return;

            double dt = (double)(now - last) / Stopwatch.Frequency;
            if (dt <= 0)
                return;

            double inst = 1.0 / dt;

            // 간단 EMA(지터 완화). Alpha=0.20
            const double alpha = 0.20;
            _fpsEma = (_fpsEma <= 0) ? inst : (_fpsEma + (inst - _fpsEma) * alpha);

            PreviewFps = _fpsEma;
        }
        private void ClearPreviewOnUi()
        {
            PreviewBitmap = null;
            _previewFullImage?.Dispose();
            _previewFullImage = null;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                Volatile.Write(ref _previewActive, 0);
                StopConsumeLoop();
                await AwaitConsumeLoopAsync().ConfigureAwait(false);
            }
            catch { }

            try
            {
                await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            try
            {
                await UiInvokeAsync(() =>
                {
                    IsPreviewing = false;
                    ClearPreviewOnUi();
                    PreviewFps = 0;
                }).ConfigureAwait(false);
            }
            catch { }
        }
    }
}