using AutoMapper.Configuration.Annotations;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaApp.Configuration;
using AvaloniaApp.Core.Models;
using AvaloniaApp.Infrastructure;
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
        // 핵심 서비스 (AppServices에서 분해하여 보관하거나 직접 참조)
        private readonly VimbaCameraService _cameraService;
        private readonly ImageProcessServiceTest _imageProcessService;
        private readonly WorkspaceService _workspaceService;
        private readonly UiThrottler _throttler;

        // 백그라운드 루프 제어용 토큰 및 태스크
        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;
        private volatile bool _stopRequested;

        // UI 렌더링용 비트맵 및 데이터
        private WriteableBitmap? _previewBitmap;
        private FrameData? _previewFrameData; // UI로 보낼 대기 중인 최신 프레임

        // FPS 계산용 변수
        private long _lastRenderTs;
        private double _fpsEma;
        // 뷰(Code-behind)에서 화면 갱신을 위해 구독하는 이벤트
        public event Action? PreviewInvalidated;

        public ObservableCollection<ComboBoxData> WavelengthIndexs { get; } 
            = new ObservableCollection<ComboBoxData>(Options.GetWavelengthIndexComboBoxData());
        public ObservableCollection<ComboBoxData> WorkingDistances { get; }
            = new ObservableCollection<ComboBoxData>();
        // UI 바인딩 속성들
        [ObservableProperty] private ObservableCollection<CameraInfo> cameras = new();
        [ObservableProperty] private CameraInfo? selectedCamera;
        [ObservableProperty] private Bitmap? previewBitmap;
        [ObservableProperty] private bool isPreviewing;
        [ObservableProperty] private double previewFps;
        [ObservableProperty] private ComboBoxData? _selectedWavelengthIndex;
        [ObservableProperty] private ComboBoxData? _selectedWorkingDistance;
        public CameraViewModelTest(AppService service) : base(service)
        {
            _cameraService = service.Camera ?? throw new ArgumentNullException("CameraService missing"); 
            _imageProcessService = service.ImageProcess;
            _workspaceService = service.WorkSpace;
            _throttler = _service.Ui.CreateThrottler();
        }
        partial void OnSelectedWavelengthIndexChanged(ComboBoxData? oldValue, ComboBoxData? newValue)
        {
            if (!IsPreviewing)
            {
                int value = 7;

                if (newValue != null) value = newValue.NumericValue;
                
                DisplayWorkspaceImage(value);
            }
        }
        public int SelectedIndex => SelectedWavelengthIndex?.NumericValue ?? 0;
        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync(
                key: "RefreshCameras",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 목록을 찾는 중입니다...");
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
                    opt.Timeout = TimeSpan.FromSeconds(5);
                });
        }

        // [명령] 프리뷰 시작
        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            // 카메라가 없으면 새로고침 시도
            if (Cameras.Count == 0) await RefreshCamerasAsync();
            if (SelectedCamera is null || IsPreviewing) return; 

            await RunOperationAsync(
                key: "PreviewStart",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("카메라 연결 및 프리뷰 시작 중...");
                    await _cameraService.StartPreviewAsync(ct, SelectedCamera.Id).ConfigureAwait(false);
                    RestartConsumeLoop();
                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = true;
                        PreviewFps = 0;
                        _fpsEma = 0;
                        _lastRenderTs = 0;
                    }).ConfigureAwait(false);
                },
                configure: opt =>
                {
                    opt.JobName = "StartPreview";
                    opt.Timeout = TimeSpan.FromSeconds(10); // 넉넉하게
                });
        }
        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            if (!IsPreviewing) return;

            await RunOperationAsync(
                key: "PreviewStop",
                backgroundWork: async (ct, ctx) =>
                {
                    ctx.ReportIndeterminate("프리뷰 정지 및 연결 해제 중...");
                    _stopRequested = true;

                    if (_consumeTask != null)
                    {
                        var finished = await Task.WhenAny(_consumeTask,Task.Delay(500));
                        if (finished != _consumeTask)
                        {
                            CancelConsumeLoop();
                            await _consumeTask;
                        }
                    }
                    await _cameraService.StopPreviewAndDisconnectAsync(ct).ConfigureAwait(false);

                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = false;
                        PreviewFps = 0;
                        DisplayWorkspaceImage(SelectedIndex);
                    });
                },
                configure: opt =>
                {
                    opt.JobName = "StopPreview";
                    opt.Timeout = TimeSpan.FromSeconds(5);
                });
        }
        private void RestartConsumeLoop()
        {
            CancelConsumeLoop();
            _stopRequested = false;
            _consumeCts = new CancellationTokenSource();
            _consumeTask = ConsumeFramesAsync(_consumeCts.Token);
        }
        private void CancelConsumeLoop()
        {
           _consumeCts?.Cancel();
        }
        private async Task ConsumeFramesAsync(CancellationToken ct)
        {
            var reader = _cameraService.Frames;

            try
            {
                while (await reader.WaitToReadAsync(ct))
                {
                    while (reader.TryRead(out var frame))
                    {
                        if(_stopRequested || ct.IsCancellationRequested)
                        {
                            await SetWorkspaceAsync(frame);
                            frame.Dispose();
                            return;
                        }
                        var old = Interlocked.Exchange(ref _previewFrameData, _imageProcessService.GetCropFrameData(frame,SelectedIndex));
                        old?.Dispose();
                        frame.Dispose();
                        _throttler.Run(UpdateUI);
                    }
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConsumeFramesAsync Error: {ex}");
            }
            finally
            {
                // 루프 종료 시 대기 중인 프레임 정리
                var pending = Interlocked.Exchange(ref _previewFrameData, null);
                pending?.Dispose();
                _throttler.Reset();
            }
        }
        private async Task SetWorkspaceAsync(FrameData fullframe)
        {
            try
            {
                var newCrops = _imageProcessService.GetCropFrameDatas(fullframe);

                FrameData? stitchFrame = null;
                try
                {
                    stitchFrame = _imageProcessService.GetStitchFrameData(newCrops);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Stitch Failed: {ex}");
                }

                // [4] Workspace 저장
                await UiInvokeAsync(() =>
                {
                    var newWorkspace = new Workspace();
                    newWorkspace.SetEntireFrameData(fullframe);
                    newWorkspace.SetCropFrameDatas(newCrops);
                    newWorkspace.SetStitchFrameData(stitchFrame);
                    // 기존꺼 Dispose 및 새거 등록
                    _workspaceService.Replace(newWorkspace);

                    Debug.WriteLine($"[Success] Workspace Replaced (Crops: {newCrops.Count}, Stitch: {stitchFrame != null})");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Workspace Save Error: {ex}");
            }
        }
        private void UpdateUI()
        {
            if (!IsPreviewing) return;
            var frame = Interlocked.Exchange(ref _previewFrameData, null);
            if (frame is null) return;
            try
            {
                EnsureSharedPreview(frame.Width, frame.Height);
                if (_previewBitmap != null)
                {
                    _imageProcessService.ConvertFrameDataToWriteableBitmap(_previewBitmap, frame);
                    //UpdateFPSUI();
                    PreviewInvalidated?.Invoke();
                }
            }
            finally { frame?.Dispose(); }
        }
        private void DisplayWorkspaceImage(int index)
        {
            var ws = _workspaceService.Current;

            if (ws is null) return;

            var frame = ws.CropFrameDatas[index];
            EnsureSharedPreview(frame.Width,frame.Height);

            if(_previewBitmap is not null)
            {
                _imageProcessService.ConvertFrameDataToWriteableBitmap(_previewBitmap,frame);
                PreviewInvalidated?.Invoke();
            }
        }
        private void EnsureSharedPreview(int width, int height)
        {
            if (_previewBitmap is not null)
            {
                var ps = _previewBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height)
                    return; // 크기 같으면 재사용

                _previewBitmap.Dispose();
                _previewBitmap = null;
            }

            _previewBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Gray8, // 흑백 카메라 기준
                AlphaFormat.Opaque);

            // 바인딩된 속성 업데이트 (화면 깜빡임 방지를 위해 교체 시에만)
            PreviewBitmap = _previewBitmap;
        }
        private void UpdateFPSUI()
        {
            long now = Stopwatch.GetTimestamp();
            long last = _lastRenderTs;
            _lastRenderTs = now;

            if (last == 0) return;

            double dt = (double)(now - last) / Stopwatch.Frequency;
            if (dt <= 0) return;

            double inst = 1.0 / dt; // 순간 FPS

            // 지수 이동 평균 (Alpha = 0.2)
            const double alpha = 0.20;
            _fpsEma = (_fpsEma <= 0) ? inst : (_fpsEma + (inst - _fpsEma) * alpha);

            PreviewFps = _fpsEma;
        }
        public override async ValueTask DisposeAsync()
        {
            // 루프 중단
            CancelConsumeLoop();
            if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);

            // 카메라 연결 해제 (안전하게)
            try
            {
                await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            // 비트맵 해제
            _previewBitmap?.Dispose();

            await base.DisposeAsync();
        }
    }
}