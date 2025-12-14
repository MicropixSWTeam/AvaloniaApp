// AvaloniaApp.Presentation/ViewModels/UserControls/CameraViewModel.cs
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaApp.Core.Interfaces;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Core.Pipelines;
using AvaloniaApp.Infrastructure;
using AvaloniaApp.Presentation.Operations;
using AvaloniaApp.Presentation.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VmbNET;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModelTest : ViewModelBase
    {
        private readonly VimbaCameraService _camera;


        // FrameReady 중복 구독 방지
        private int _subscribed;


        // UI 갱신 과부하 방지(대략 30fps)
        private long _lastUiTick;


        [ObservableProperty] private ObservableCollection<CameraInfo> cameras = new();
        [ObservableProperty] private CameraInfo? selectedCamera;


        // Image.Source로 바인딩
        [ObservableProperty] private Bitmap? previewBitmap;


        [ObservableProperty] private bool isPreviewing;


        public CameraViewModelTest(
        VimbaCameraService camera,
        UiDispatcher ui,
        OperationRunner runner)
        : base(ui, runner)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }


        private bool CanStartPreview()
        => !IsPreviewing && SelectedCamera is not null;


        private bool CanStopPreview()
        => IsPreviewing;
        partial void OnSelectedCameraChanged(CameraInfo? value)
        {
            StartPreviewCommand.NotifyCanExecuteChanged();
            StopPreviewCommand.NotifyCanExecuteChanged();
        }


        partial void OnIsPreviewingChanged(bool value)
        {
            StartPreviewCommand.NotifyCanExecuteChanged();
            StopPreviewCommand.NotifyCanExecuteChanged();
        }


        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync(
            key: "camera.refresh",
            backgroundWork: async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("카메라 목록 불러오는 중...");
                var list = await _camera.GetCameraListAsync(ct).ConfigureAwait(false);


                await UiAsync(() =>
                {
                    Cameras.Clear();
                    foreach (var c in list)
                        Cameras.Add(c);


                    // 첫 항목 자동 선택(원하면 제거)
                    SelectedCamera ??= Cameras.FirstOrDefault();
                }).ConfigureAwait(false);
            },
            configure: opt =>
            {
                opt.JobName = "GetCameraList";
                opt.StartMessage = "카메라 검색";
                opt.Timeout = TimeSpan.FromSeconds(3);
            });
        }
        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            var cam = SelectedCamera ?? throw new InvalidOperationException("카메라를 선택하세요.");


            await RunOperationAsync(
            key: "preview.start",
            backgroundWork: async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("연결 중...");
                await _camera.ConnectAsync(ct, cam.Id).ConfigureAwait(false);


                // 구독은 Start 전에 붙여야 첫 프레임을 놓치지 않음
                EnsureSubscribed();


                ctx.ReportIndeterminate("프리뷰 시작 중...");
                await _camera.StartStreamAsync(ct).ConfigureAwait(false);

                await UiAsync(() => IsPreviewing = true).ConfigureAwait(false);
            },
            configure: opt =>
            {
                opt.JobName = "StartPreview";
                opt.StartMessage = "프리뷰 시작";
                opt.Timeout = TimeSpan.FromSeconds(5);
            });
        }


        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            await RunOperationAsync(
            key: "preview.stop",
            backgroundWork: async (ct, ctx) =>
            {
                ctx.ReportIndeterminate("프리뷰 정지 중...");


                // Stop/Disconnect 순서
                await _camera.StopStreamAsync(ct).ConfigureAwait(false);
                await _camera.DisconnectAsync(ct).ConfigureAwait(false);


                // 이벤트 해제(Stop 이후 마지막 프레임이 올 수 있으므로 IsPreviewing false 먼저)
                await UiAsync(() =>
                {
                    IsPreviewing = false;
                    ClearPreviewOnUi();
                }).ConfigureAwait(false);


                RemoveSubscription();
            },
            configure: opt =>
            {
                opt.JobName = "StopPreview";
                opt.StartMessage = "프리뷰 정지";
                opt.Timeout = TimeSpan.FromSeconds(5);
            });
        }
        private void EnsureSubscribed()
        {
            if (Interlocked.Exchange(ref _subscribed, 1) == 1)
                return;


            _camera.FrameReady += OnFrameReady;
        }


        private void RemoveSubscription()
        {
            if (Interlocked.Exchange(ref _subscribed, 0) == 0)
                return;


            _camera.FrameReady -= OnFrameReady;
        }


        private void OnFrameReady(object? sender, Bitmap bmp)
        {
            // Stop 직후 늦게 도착한 프레임은 폐기
            if (!IsPreviewing)
            {
                bmp.Dispose();
                return;
            }


            // UI 갱신 throttling (약 30fps)
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastUiTick);
            if (now - last < 33)
            {
                bmp.Dispose();
                return;
            }
            Interlocked.Exchange(ref _lastUiTick, now);


            _ui.Post(() =>
            {
                // UI 스레드에서 최신 프레임으로 교체 + 이전 Dispose
                var old = PreviewBitmap;
                PreviewBitmap = bmp;
                old?.Dispose();
            });
        }


        private void ClearPreviewOnUi()
        {
            var old = PreviewBitmap;
            PreviewBitmap = null;
            old?.Dispose();
        }
        public async ValueTask DisposeAsync()
        {
            // 뷰가 닫히거나 VM이 파기될 때 안전 정리
            try
            {
                if (IsPreviewing)
                {
                    // Dispose에서는 ct를 쓸 수 없으므로 CancellationToken.None
                    await _camera.StopStreamAsync(CancellationToken.None).ConfigureAwait(false);
                    await _camera.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch { }


            RemoveSubscription();


            try
            {
                await UiAsync(() =>
                {
                    IsPreviewing = false;
                    ClearPreviewOnUi();
                }).ConfigureAwait(false);
            }
            catch { }
        }
    }
}