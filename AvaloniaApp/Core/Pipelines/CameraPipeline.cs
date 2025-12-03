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

        public CameraPipeline(
            BackgroundJobQueue backgroundJobQueue,
            VimbaCameraService cameraService,
            UiDispatcher uiDispatcher)
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
        }

        // 그대로 사용
        public Task EnqueueGetCameraListAsync(
            CancellationToken ct,
            Func<IReadOnlyList<CameraInfo>, Task> onGetCameraList)
        {
            var job = new BackgroundJob("GetCameraList",
                async token =>
                {
                    var list = await _cameraService.GetCameraListAsync(token);
                    await _uiDispatcher.InvokeAsync(() => onGetCameraList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        // 그대로 사용
        public Task EnqueueGetPixelFormatListAsync(
            CancellationToken ct,
            string id,
            Func<IReadOnlyList<PixelFormatInfo>, Task> onGetPixelFormatList)
        {
            var job = new BackgroundJob("GetPixelFormatList",
                async token =>
                {
                    var list = await _cameraService.GetSupportPixelformatListAsync(token, id);
                    await _uiDispatcher.InvokeAsync(() => onGetPixelFormatList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        /// <summary>
        /// 연속 프리뷰 시작 (FrameReady 이벤트 구독)
        /// </summary>
        public Task EnqueueStartPreviewAsync(
            CancellationToken ct,
            Func<Bitmap, Task> onFrame)
        {
            var job = new BackgroundJob(
                "CameraPreview",
                async token =>
                {
                    // 이벤트 핸들러: Vimba 쓰레드 → UI 쓰레드
                    async void Handler(object? sender, Bitmap bmp)
                    {
                        if (token.IsCancellationRequested)
                        {
                            // 이미 취소된 상태면 이 프레임은 사용하지 않고 폐기
                            bmp.Dispose();
                            return;
                        }

                        await _uiDispatcher.InvokeAsync(() => onFrame(bmp));
                    }

                    _cameraService.FrameReady += Handler;

                    try
                    {
                        await _cameraService.StartStreamAsync(token);

                        // 토큰이 취소될 때까지 단순 대기
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(50, token);
                        }
                    }
                    finally
                    {
                        _cameraService.FrameReady -= Handler;
                        await _cameraService.StopStreamAsync(CancellationToken.None);
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueStopPreviewAsync(
            CancellationToken ct)
        {
            var job = new BackgroundJob(
                "CameraStopPreview",
                async token =>
                {
                    await _cameraService.StopStreamAsync(token);
                });
            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        /// <summary>
        /// 단일 캡처
        /// </summary>
        public Task EnqueueCaptureAsync(
            CancellationToken ct,
            Func<Bitmap, Task> onCapture)
        {
            var job = new BackgroundJob("CameraCapture",
                async token =>
                {
                    var bmp = await _cameraService.CaptureAsync(token);
                    await _uiDispatcher.InvokeAsync(() => onCapture(bmp));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        /// <summary>
        /// 카메라 연결 (UI 콜백 제대로 호출)
        /// </summary>
        public Task EnqueueConnectAsync(
            CancellationToken ct,
            string id,
            Func<Task> onConnect)
        {
            var job = new BackgroundJob("CameraConnect",
                async token =>
                {
                    await _cameraService.ConnectAsync(token, id);

                    await _uiDispatcher.InvokeAsync(onConnect);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        public Task EnqueueDisconnectAsync(
            CancellationToken ct,
            Func<Task>? onDisconnect = null)
        {
            var job = new BackgroundJob("CameraDisconnect",
                async token =>
                {
                    await _cameraService.DisconnectAsync(token);

                    if (onDisconnect is not null)
                    {
                        await _uiDispatcher.InvokeAsync(onDisconnect);
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
    }
}
