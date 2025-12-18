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
using LiveChartsCore.Kernel;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaApp.Presentation.ViewModels.UserControls
{
    public partial class CameraViewModel : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        private readonly ImageProcessService _imageProcessService;
        private readonly WorkspaceService _workspaceService;
        private readonly RegionAnalysisService _regionAnalysisService;
        private readonly UiThrottler _throttler;
        private readonly Options _options;

        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;
        private volatile bool _stopRequested;

        private WriteableBitmap? _previewBitmap;
        private FrameData? _previewFrameData;

        private long _lastRenderTs;
        private double _fpsEma;

        public event Action? PreviewInvalidated;

        public ReadOnlyObservableCollection<SelectRegionData> Regions => _regionAnalysisService.Regions;

        public ObservableCollection<ComboBoxData> WavelengthIndexs { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWavelengthIndexComboBoxData());
        
        public ObservableCollection<ComboBoxData> WorkingDistances { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWorkingDistanceComboBoxData());

        [ObservableProperty] private ObservableCollection<CameraInfo> cameras = new();
        [ObservableProperty] private CameraInfo? selectedCamera;
        [ObservableProperty] private Bitmap? previewBitmap;
        [ObservableProperty] private bool isPreviewing;
        [ObservableProperty] private double previewFps;
        [ObservableProperty] private ComboBoxData? _selectedWavelengthIndex;
        [ObservableProperty] private ComboBoxData? _selectedWorkingDistance;
        public int CropWidth => _options.CropWidth;
        public int CropHeight => _options.CropHeight;

        public CameraViewModel(AppService service) : base(service)
        {
            _cameraService = service.Camera ?? throw new ArgumentNullException("CameraService missing");
            _imageProcessService = service.ImageProcess;
            _workspaceService = service.WorkSpace;
            _regionAnalysisService = service.RegionAnalysis;
            _options = service.Options;
            _throttler = _service.Ui.CreateThrottler();
            SelectedWavelengthIndex = WavelengthIndexs.FirstOrDefault();
            SelectedWorkingDistance = WorkingDistances.FirstOrDefault();
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
        partial void OnSelectedWorkingDistanceChanged(ComboBoxData? oldValue, ComboBoxData? newValue)
        {
            if (newValue == null) return;
        }

        [RelayCommand]
        public void AddRoi(Rect controlRect)
        {
            _regionAnalysisService.AddRoi(controlRect);
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

        [RelayCommand]
        private async Task StartPreviewAsync()
        {
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
                    opt.Timeout = TimeSpan.FromSeconds(10);
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
                        var finished = await Task.WhenAny(_consumeTask, Task.Delay(500));
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
                        if (_stopRequested || ct.IsCancellationRequested)
                        {
                            UpdateWorkspaceBackground(frame);
                            frame.Dispose();
                            return;
                        }
                        var oldUiFrame = Interlocked.Exchange(ref _previewFrameData, _imageProcessService.GetCropFrameData(frame, SelectedIndex));
                        oldUiFrame?.Dispose();
                        frame.Dispose();
                        _throttler.Run(UpdateUI);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex){ }
            finally
            {
                var pending = Interlocked.Exchange(ref _previewFrameData, null);
                pending?.Dispose();
                _throttler.Reset();
            }
        }
        /// <summary>
        /// 백그라운드 스레드에서 Workspace 데이터를 처리하고 교체합니다.
        /// </summary>
        private void UpdateWorkspaceBackground(FrameData fullframe)
        {
            try
            {
                // [Background Thread] 무거운 이미지 처리
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

                // Workspace 객체 생성
                var newWorkspace = new Workspace();

                // [중요] Service의 Clone 기능을 사용 (내부적으로 ArrayPool 최적화됨)
                newWorkspace.SetEntireFrameData(_imageProcessService.CloneFrameData(fullframe));
                newWorkspace.SetCropFrameDatas(newCrops);
                newWorkspace.SetStitchFrameData(stitchFrame);

                // [Thread-Safe] 교체
                _workspaceService.Replace(newWorkspace);
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
                    UpdateFPSUI();
                    PreviewInvalidated?.Invoke();
                }
            }
            finally { frame?.Dispose(); }
        }
        private void DisplayWorkspaceImage(int index)
        {
            var ws = _workspaceService.Current;

            if (ws is null || ws.CropFrameDatas.Count <= index) return;

            var frame = ws.CropFrameDatas[index];
            EnsureSharedPreview(frame.Width, frame.Height);

            if (_previewBitmap is not null)
            {
                _imageProcessService.ConvertFrameDataToWriteableBitmap(_previewBitmap, frame);
                PreviewInvalidated?.Invoke();
            }
        }
        private void EnsureSharedPreview(int width, int height)
        {
            if (_previewBitmap is not null)
            {
                var ps = _previewBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height)
                    return;

                _previewBitmap.Dispose();
                _previewBitmap = null;
            }

            _previewBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Gray8,
                AlphaFormat.Opaque);

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

            double inst = 1.0 / dt;
            const double alpha = 0.20;
            _fpsEma = (_fpsEma <= 0) ? inst : (_fpsEma + (inst - _fpsEma) * alpha);

            PreviewFps = _fpsEma;
        }
        public override async ValueTask DisposeAsync()
        {
            CancelConsumeLoop();
            if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);

            try
            {
                await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            _previewBitmap?.Dispose();

            await base.DisposeAsync();
        }
    }
}