using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Jobs;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;

namespace AvaloniaApp.Core.Pipelines
{
    /// <summary>
    /// 카메라 관련 비동기 작업을 BackgroundJobQueue + UiDispatcher를 통해 실행하는 파이프라인입니다.
    /// </summary>
    public class CameraPipeline
    {
        private readonly BackgroundJobQueue _backgroundJobQueue;
        private readonly VimbaCameraService _cameraService;
        private readonly UiDispatcher _uiDispatcher;

        private EventHandler<Bitmap>? _previewHandler;
        private readonly object _sync = new();
        private bool _previewRunning;

        /// <summary>
        /// CameraPipeline을 생성합니다.
        /// </summary>
        /// <param name="backgroundJobQueue">카메라 I/O를 실행할 백그라운드 큐.</param>
        /// <param name="cameraService">실제 Vimba 카메라 서비스를 캡슐화한 서비스.</param>
        /// <param name="uiDispatcher">UI 업데이트용 Dispatcher 래퍼.</param>
        public CameraPipeline(
            BackgroundJobQueue backgroundJobQueue,
            VimbaCameraService cameraService,
            UiDispatcher uiDispatcher)
        {
            _backgroundJobQueue = backgroundJobQueue;
            _cameraService = cameraService;
            _uiDispatcher = uiDispatcher;
        }

        /// <summary>
        /// 카메라 리스트를 조회하는 작업을 큐에 등록합니다.
        /// 완료 후 <paramref name="onGetCameraList"/>를 UI 스레드에서 호출합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onGetCameraList">조회된 카메라 리스트를 처리할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueGetCameraListAsync(
            CancellationToken ct,
            Func<IReadOnlyList<CameraInfo>, Task> onGetCameraList)
        {
            var job = new BackgroundJob(
                "GetCameraList",
                async token =>
                {
                    var list = await _cameraService.GetCameraListAsync(token).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(() => onGetCameraList(list)).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 지정한 카메라 ID에 대해 지원하는 PixelFormat 리스트를 조회하는 작업을 큐에 등록합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="id">카메라 ID.</param>
        /// <param name="onGetPixelFormatList">조회된 포맷 리스트를 처리할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueGetPixelFormatListAsync(
            CancellationToken ct,
            string id,
            Func<IReadOnlyList<PixelFormatInfo>, Task> onGetPixelFormatList)
        {
            var job = new BackgroundJob(
                "GetPixelFormatList",
                async token =>
                {
                    var list = await _cameraService.GetSupportPixelformatListAsync(token, id).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(() => onGetPixelFormatList(list)).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라 프리뷰 스트림을 시작하는 작업을 큐에 등록합니다.
        /// 프레임이 도착할 때마다 <paramref name="onFrame"/>을 UI 스레드에서 호출합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onFrame">새 Bitmap 프레임을 처리할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueStartPreviewAsync(
            CancellationToken ct,
            Func<Bitmap, Task> onFrame)
        {
            var job = new BackgroundJob(
                "CameraPreviewStart",
                async token =>
                {
                    try
                    {
                        if (_cameraService.ConnectedCameraInfo is null)
                            throw new InvalidOperationException("카메라가 연결되지 않았습니다.");

                        lock (_sync)
                        {
                            if (_previewRunning)
                                return;
                        }

                        async void Handler(object? sender, Bitmap bmp)
                        {
                            try
                            {
                                if (token.IsCancellationRequested)
                                {
                                    bmp.Dispose();
                                    return;
                                }

                                await _uiDispatcher.InvokeAsync(() => onFrame(bmp)).ConfigureAwait(false);
                            }
                            catch
                            {
                                bmp.Dispose();
                            }
                        }

                        lock (_sync)
                        {
                            if (_previewHandler is not null)
                            {
                                _cameraService.FrameReady -= _previewHandler;
                                _previewHandler = null;
                            }

                            _previewHandler = Handler;
                            _cameraService.FrameReady += _previewHandler;
                            _previewRunning = true;
                        }

                        await _cameraService.StartStreamAsync(token).ConfigureAwait(false);
                    }
                    catch
                    {
                        lock (_sync)
                        {
                            if (_previewHandler is not null)
                            {
                                _cameraService.FrameReady -= _previewHandler;
                                _previewHandler = null;
                            }

                            _previewRunning = false;
                        }

                        throw;
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라 프리뷰 스트림을 중지하고, 핸들러를 해제하는 작업을 큐에 등록합니다.
        /// 완료 후 <paramref name="onStopPreview"/>를 UI 스레드에서 호출합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onStopPreview">중지 후 UI를 업데이트할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueStopPreviewAsync(CancellationToken ct, Func<Task> onStopPreview)
        {
            var job = new BackgroundJob(
                "CameraStopPreview",
                async token =>
                {
                    lock (_sync)
                    {
                        if (_previewHandler is not null)
                        {
                            _cameraService.FrameReady -= _previewHandler;
                            _previewHandler = null;
                        }

                        _previewRunning = false;
                    }

                    await _cameraService.StopStreamAsync(token).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(onStopPreview).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 한 프레임을 캡처하는 작업을 큐에 등록합니다.
        /// 캡처된 Bitmap은 <paramref name="onCapture"/>에서 처리합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onCapture">캡처된 Bitmap을 처리할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueCaptureAsync(CancellationToken ct, Func<Bitmap, Task> onCapture)
        {
            var job = new BackgroundJob(
                "CameraCapture",
                async token =>
                {
                    var bmp = await _cameraService.CaptureAsync(token).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(() => onCapture(bmp)).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라에 연결하는 작업을 큐에 등록합니다.
        /// 성공 후 <paramref name="onConnect"/>를 UI 스레드에서 호출합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="id">연결할 카메라 ID.</param>
        /// <param name="onConnect">연결 후 UI를 갱신할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueConnectAsync(CancellationToken ct, string id, Func<Task> onConnect)
        {
            var job = new BackgroundJob(
                "CameraConnect",
                async token =>
                {
                    await _cameraService.ConnectAsync(token, id).ConfigureAwait(false);
                    await _uiDispatcher.InvokeAsync(onConnect).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라 스트림과 연결을 해제하는 작업을 큐에 등록합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onDisconnect">연결 해제 후 UI를 갱신할 콜백 (null 가능).</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueDisconnectAsync(CancellationToken ct, Func<Task>? onDisconnect = null)
        {
            var job = new BackgroundJob(
                "CameraDisconnect",
                async token =>
                {
                    lock (_sync)
                    {
                        if (_previewHandler is not null)
                        {
                            _cameraService.FrameReady -= _previewHandler;
                            _previewHandler = null;
                        }

                        _previewRunning = false;
                    }

                    await _cameraService.StopStreamAsync(token).ConfigureAwait(false);
                    await _cameraService.DisconnectAsync(token).ConfigureAwait(false);

                    if (onDisconnect is not null)
                        await _uiDispatcher.InvokeAsync(onDisconnect).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라에서 노출/게인/감마 등의 파라미터를 읽어오는 작업을 큐에 등록합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="onLoaded">읽어온 값을 UI에서 처리할 콜백.</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
        public Task EnqueueLoadCameraParamsAsync(CancellationToken ct, Func<double, double, double, Task> onLoaded)
        {
            var job = new BackgroundJob(
                "CameraLoadParams",
                async token =>
                {
                    var exposure = await _cameraService.GetExposureTimeAsync(token).ConfigureAwait(false);
                    var gain = await _cameraService.GetGainAsync(token).ConfigureAwait(false);
                    var gamma = await _cameraService.GetGammaAsync(token).ConfigureAwait(false);

                    await _uiDispatcher.InvokeAsync(() => onLoaded(exposure, gain, gamma)).ConfigureAwait(false);
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }

        /// <summary>
        /// 카메라 파라미터(노출/게인/감마)를 설정하는 작업을 큐에 등록합니다.
        /// </summary>
        /// <param name="ct">작업 취소 토큰.</param>
        /// <param name="exposureTime">설정할 노출 시간.</param>
        /// <param name="gain">설정할 게인.</param>
        /// <param name="gamma">설정할 감마.</param>
        /// <param name="onApplied">실제 적용된 값을 UI에서 처리할 콜백 (null 가능).</param>
        /// <returns>작업 완료를 나타내는 Task.</returns>
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
                    var appliedExposure = await _cameraService.SetExposureTimeAsync(exposureTime, token).ConfigureAwait(false);
                    var appliedGain = await _cameraService.SetGainAsync(gain, token).ConfigureAwait(false);
                    var appliedGamma = await _cameraService.SetGammaAsync(gamma, token).ConfigureAwait(false);

                    if (onApplied is not null)
                    {
                        await _uiDispatcher
                            .InvokeAsync(() => onApplied(appliedExposure, appliedGain, appliedGamma))
                            .ConfigureAwait(false);
                    }
                });

            return _backgroundJobQueue.EnqueueAsync(job, ct);
        }
    }
}
