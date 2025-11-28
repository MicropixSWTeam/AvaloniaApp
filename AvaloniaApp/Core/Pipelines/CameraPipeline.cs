using AutoMapper.Configuration.Annotations;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Infrastructure;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Pipelines
{
    public class CameraPipeline
    {
        private readonly IBackgroundJobQueue _backgroundJobQueue;
        private readonly VimbaCameraService _cameraService;
        private readonly IUiDispatcher _uiDispatcher;

        public CameraPipeline(IBackgroundJobQueue backgroundJobQueue, VimbaCameraService cameraService,IUiDispatcher uiDispatcher
        )
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
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

        public Task EnqueueConnectAsync(CancellationToken ct)
        {
            var job = new BackgroundJob("CameraConnect",
                async token =>
                {
                    await _cameraService.ConnectAsync(token);

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
