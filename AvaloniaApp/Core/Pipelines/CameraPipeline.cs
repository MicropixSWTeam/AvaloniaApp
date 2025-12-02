using AutoMapper.Configuration.Annotations;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
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

        public CameraPipeline(BackgroundJobQueue backgroundJobQueue, VimbaCameraService cameraService,UiDispatcher uiDispatcher)
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
        }
        public Task EnqueueGetCameraListAsync(CancellationToken ct,Func<IReadOnlyList<CameraInfo>, Task> OnGetCameraList)
        {
            var job = new BackgroundJob("GetCameraList",
                async token =>
                {
                    var list = await _cameraService.GetCameraListAsync(token);
                    // ViewModel 업데이트는 UI 스레드에서
                    await _uiDispatcher.InvokeAsync(() => OnGetCameraList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
        public Task EnqueueGetPixelFormatListAsync(CancellationToken ct, string id, Func<IReadOnlyList<PixelFormatInfo>, Task> OnGetPixelFormatList)
        {
            var job = new BackgroundJob("GetPixelFormatList",
                async token =>
                {
                    var list = await _cameraService.GetSupportPixelformatListAsync(token, id);

                    await _uiDispatcher.InvokeAsync(() => OnGetPixelFormatList(list));
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }    
        public Task EnqueueCaptureAsync(CancellationToken ct,Func<Bitmap,Task> OnCapture)
        {
            var job = new BackgroundJob("CameraCapture", 
                async token =>
                {
                    await _cameraService.CaptureAsync(token);
                    var bitmap = new Bitmap("");
                    await OnCapture(bitmap);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        public Task EnqueueConnectAsync(CancellationToken ct,string id)
        {
            var job = new BackgroundJob("CameraConnect",
                async token =>
                {
                    await _cameraService.ConnectAsync(token,id);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        return Task.CompletedTask;
                    });
                });
            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }

        public Task EnqueueDisconnectAsync(CancellationToken ct)
        {
            var job = new BackgroundJob("CameraDisconnect",
                async token =>
                {
                    await _cameraService.DisconnectAsync(token);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        return Task.CompletedTask;
                    });
                });
            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
    }
}
