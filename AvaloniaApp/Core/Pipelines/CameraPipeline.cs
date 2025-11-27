using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Jobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Core.Pipelines
{
    public class CameraPipeline
    {
        private readonly IBackgroundJobQueue _backgroundJobQueue;
        private readonly ICameraService _cameraService;
        private readonly IUiDispatcher _uiDispatcher;

        public CameraPipeline(
            IBackgroundJobQueue backgroundJobQueue,
            ICameraService cameraService,
            IUiDispatcher uiDispatcher
        )
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
        }

        public Task EnqueueCaptureAsync(CancellationToken ct)
        {
            var job = new BackgroundJob("CameraCapture", 
                async token =>
                {
                    await _cameraService.CaptureAsync(token);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        // capture 로직
                        return Task.CompletedTask;
                    });
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
                        // capture 로직
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
                        // capture 로직
                        return Task.CompletedTask;
                    });
                });
            return _backgroundJobQueue.EnqueueAsync(job, ct).AsTask();
        }
    }
}
