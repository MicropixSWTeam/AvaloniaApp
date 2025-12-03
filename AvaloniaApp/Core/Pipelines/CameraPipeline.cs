using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Pipelines
{
    public class CameraPipeline
    {
        private readonly BackgroundJobQueue _backgroundJobQueue;
        private readonly VimbaCameraService _cameraService;
        private readonly UiDispatcher _uiDispatcher;
        private EventHandler<Bitmap>? _previewHandler;
        private readonly object _sync = new();
        public CameraPipeline(
            BackgroundJobQueue backgroundJobQueue,
            VimbaCameraService cameraService,
            UiDispatcher uiDispatcher)
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
        }
        public Task EnqueueGetCameraListAsync(CancellationToken ct,Func<IReadOnlyList<CameraInfo>, Task> onGetCameraList)
        {
            var job = new BackgroundJob("GetCameraList",
                async token =>
                {
                    var list = await _cameraService.GetCameraListAsync(token);
                    await _uiDispatcher.InvokeAsync(() => onGetCameraList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueGetPixelFormatListAsync(CancellationToken ct,string id,Func<IReadOnlyList<PixelFormatInfo>, Task> onGetPixelFormatList)
        {
            var job = new BackgroundJob("GetPixelFormatList",
                async token =>
                {
                    var list = await _cameraService.GetSupportPixelformatListAsync(token, id);
                    await _uiDispatcher.InvokeAsync(() => onGetPixelFormatList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueStartPreviewAsync(CancellationToken ct,Func<Bitmap, Task> onFrame)
        {
            var job = new BackgroundJob(
                "CameraPreviewStart",
                async token =>
                {
                    // Vimba 스레드 → UI 스레드로 전달하는 핸들러
                    async void Handler(object? sender, Bitmap bmp)
                    {
                        // 앱 종료 시점에만 사용되는 토큰이므로, 프레임 폐기 정도만 처리
                        if (token.IsCancellationRequested)
                        {
                            bmp.Dispose();
                            return;
                        }

                        await _uiDispatcher.InvokeAsync(() => onFrame(bmp));
                    }

                    // 기존 핸들러 제거 후 새 핸들러 등록 (중복 방지)
                    lock (_sync)
                    {
                        if (_previewHandler is not null)
                        {
                            _cameraService.FrameReady -= _previewHandler;
                        }

                        _previewHandler = Handler;
                        _cameraService.FrameReady += _previewHandler;
                    }

                    // 여기서 스트림만 시작하고, 더 이상 잡을 붙들고 있을 필요 없음
                    await _cameraService.StartStreamAsync(CancellationToken.None);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueStopPreviewAsync(CancellationToken ct)
        {
            var job = new BackgroundJob(
                "CameraStopPreview",
                async token =>
                {
                    // 스트림 먼저 정지
                    await _cameraService.StopStreamAsync(CancellationToken.None);

                    // FrameReady 핸들러도 해제
                    lock (_sync)
                    {
                        if (_previewHandler is not null)
                        {
                            _cameraService.FrameReady -= _previewHandler;
                            _previewHandler = null;
                        }
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueCaptureAsync(CancellationToken ct,Func<Bitmap, Task> onCapture)
        {
            var job = new BackgroundJob("CameraCapture",
                async token =>
                {
                    var bmp = await _cameraService.CaptureAsync(token);
                    await _uiDispatcher.InvokeAsync(() => onCapture(bmp));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueConnectAsync(CancellationToken ct,string id,Func<Task> onConnect)
        {
            var job = new BackgroundJob("CameraConnect",
                async token =>
                {
                    await _cameraService.ConnectAsync(token, id);

                    await _uiDispatcher.InvokeAsync(onConnect);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueDisconnectAsync(CancellationToken ct,Func<Task>? onDisconnect = null)
        {
            var job = new BackgroundJob("CameraDisconnect",
                async token =>
                {
                    // 프리뷰가 돌고 있을 수 있으니 먼저 정리
                    await _cameraService.StopStreamAsync(CancellationToken.None);

                    lock (_sync)
                    {
                        if (_previewHandler is not null)
                        {
                            _cameraService.FrameReady -= _previewHandler;
                            _previewHandler = null;
                        }
                    }
                    // 카메라 연결 해제
                    await _cameraService.DisconnectAsync(token);

                    if (onDisconnect is not null)
                    {
                        await _uiDispatcher.InvokeAsync(onDisconnect);
                    }
                });
            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueLoadCameraParamsAsync(
            CancellationToken ct,
            Func<double, double, double, Task> onLoaded)
        {
            var job = new BackgroundJob(
                "CameraLoadParams",
                async token =>
                {
                    var exposure = await _cameraService.GetExposureTimeAsync(token);
                    var gain = await _cameraService.GetGainAsync(token);
                    var gamma = await _cameraService.GetGammaAsync(token);

                    await _uiDispatcher.InvokeAsync(() => onLoaded(exposure, gain, gamma));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueApplyCameraParamsAsync(
            CancellationToken ct,
            double exposureTime,
            double gain,
            double gamma,
            Func<double, double, double, Task>? onApplied = null)
        {
            var job = new BackgroundJob(
                "CameraApplyParams",
                async token =>
                {
                    var appliedExposure = await _cameraService.SetExposureTimeAsync(exposureTime, token);
                    var appliedGain = await _cameraService.SetGainAsync(gain, token);
                    var appliedGamma = await _cameraService.SetGammaAsync(gamma, token);

                    if (onApplied is not null)
                    {
                        await _uiDispatcher.InvokeAsync(
                            () => onApplied(appliedExposure, appliedGain, appliedGamma));
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
    }
}
