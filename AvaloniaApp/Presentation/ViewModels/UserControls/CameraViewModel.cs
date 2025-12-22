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
    public partial class CameraViewModel : ViewModelBase
    {
        private readonly VimbaCameraService _cameraService;
        private readonly ImageProcessService _imageProcessService;
        private readonly WorkspaceService _workspaceService;
        private readonly UiThrottler _uiThrottler;

        private CancellationTokenSource? _consumeCts;
        private Task? _consumeTask;
        private volatile bool _stopRequested;

        private int _isAnalyzing = 0;

        private WriteableBitmap? _previewBitmap;
        private FrameData? _previewFrameData;

        private WriteableBitmap? _rgbBitmap;
        private FrameData? _rgbFrameData;

        public event Action? PreviewInvalidated;
        public event Action? RgbPreviewInvalidated;

        public ReadOnlyObservableCollection<RegionData>? Regions => _workspaceService.Current?.RegionDatas;
        public int NextAvailableRegionColorIndex => _workspaceService.Current?.GetNextAvailableIndex() ?? -1;
        public bool IsChartTooltipEnabled => !IsPreviewing;

        public ObservableCollection<ComboBoxData> WavelengthIndexs { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWavelengthIndexComboBoxData());
        public ObservableCollection<ComboBoxData> WorkingDistances { get; }
            = new ObservableCollection<ComboBoxData>(Options.GetWorkingDistanceComboBoxData());

        [ObservableProperty] private ObservableCollection<CameraData> cameras = new();
        [ObservableProperty] private CameraData? selectedCamera;
        [ObservableProperty] private Bitmap? previewBitmap;
        [ObservableProperty] private Bitmap? rgbBitmap;
        [ObservableProperty] private bool isPreviewing;
        [ObservableProperty] private double previewFps;
        [ObservableProperty] private ComboBoxData? _selectedWavelengthIndex;
        [ObservableProperty] private ComboBoxData? _selectedWorkingDistance;

        public int CurrentWavelengthIndex => SelectedWavelengthIndex?.NumericValue ?? Options.DefaultWavelengthIndex;
        public int CurrentWorkingDistance => SelectedWorkingDistance?.NumericValue ?? Options.DefaultWorkingDistance;
        public int CropWidth => Options.CropWidthSize;
        public int CropHeight => Options.CropHeightSize;

        public CameraViewModel(AppService service) : base(service)
        {
            _cameraService = service.Camera ?? throw new ArgumentNullException();
            _imageProcessService = service.ImageProcess;
            _workspaceService = service.WorkSpace;
            _uiThrottler = _service.Ui.CreateThrottler();

            SelectedWavelengthIndex = WavelengthIndexs.FirstOrDefault();
            SelectedWorkingDistance = WorkingDistances.FirstOrDefault();

            _workspaceService.Update(ws => ws.SetWorkingDistance(CurrentWorkingDistance));
        }

        partial void OnSelectedWavelengthIndexChanged(ComboBoxData? oldValue, ComboBoxData? newValue) => UpdateDisplayIfStopped();
        partial void OnSelectedWorkingDistanceChanged(ComboBoxData? oldValue, ComboBoxData? newValue) => UpdateDisplayIfStopped();
        partial void OnIsPreviewingChanged(bool value) { OnPropertyChanged(nameof(IsChartTooltipEnabled)); }

        private void UpdateDisplayIfStopped()
        {
            if (!IsPreviewing)
                _service.Ui.InvokeAsync(() => DisplayWorkspaceImage(CurrentWavelengthIndex, CurrentWorkingDistance));
        }

        [RelayCommand]
        public async Task AddRegion(Rect rect)
        {
            _workspaceService.Update(ws => ws.AddRegionData(rect));
            if (!IsPreviewing) await CalculateIntensityDatasAsync();
        }

        [RelayCommand]
        public async Task RemoveRegion(RegionData region)
        {
            if (region == null) return;
            _workspaceService.Update(ws => ws.RemoveRegionData(region));
            // 정지 상태여도 삭제 반영을 위해 분석 실행
            if (!IsPreviewing) await CalculateIntensityDatasAsync();
        }

        [RelayCommand]
        private async Task RefreshCamerasAsync()
        {
            await RunOperationAsync("RefreshCameras", async (ct, ctx) =>
            {
                var list = await _cameraService.GetCameraListAsync(ct);
                await UiInvokeAsync(() =>
                {
                    Cameras.Clear();
                    foreach (var c in list) Cameras.Add(c);
                    SelectedCamera ??= Cameras.FirstOrDefault();
                });
            });
        }

        [RelayCommand]
        private async Task StartPreviewAsync()
        {
            if (IsPreviewing) return;

            if (Cameras.Count == 0 || SelectedCamera == null)
            {
                await RefreshCamerasAsync();
                if (SelectedCamera == null) return;
            }

            await RunOperationAsync("PreviewStart", async (ct, ctx) =>
            {
                const int MaxRetries = 100;
                bool isConnected = false;
                Exception? lastException = null;

                // [안정성] 연결 재시도 로직
                for (int i = 0; i < MaxRetries; i++)
                {
                    try
                    {
                        await _cameraService.StartPreviewAsync(ct, SelectedCamera.Id);
                        isConnected = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Debug.WriteLine($"Camera connection attempt {i + 1} failed: {ex.Message}");
                        if (i < MaxRetries - 1) await Task.Delay(500 * (i + 1), ct);
                    }
                }

                if (!isConnected)
                {
                    Debug.WriteLine("All connection attempts failed.");
                    if (lastException != null) throw lastException;
                    return;
                }

                RestartConsumeLoop();
                await UiInvokeAsync(() => IsPreviewing = true);
            });
        }

        [RelayCommand]
        private async Task StopPreviewAsync()
        {
            if (!IsPreviewing) return;

            await RunOperationAsync("PreviewStop", async (ct, ctx) =>
            {
                try
                {
                    _stopRequested = true;
                    CancelConsumeLoop();

                    if (_consumeTask != null)
                        await Task.WhenAny(_consumeTask, Task.Delay(2000));

                    await _cameraService.StopPreviewAndDisconnectAsync(ct);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Stop preview failed: {ex.Message}");
                }
                finally
                {
                    await UiInvokeAsync(() =>
                    {
                        IsPreviewing = false;
                        DisplayWorkspaceImage(CurrentWavelengthIndex, CurrentWorkingDistance);
                    });
                }
            });
        }

        private async Task CalculateIntensityDatasAsync()
        {
            await RunOperationAsync("CalculateIntensityDatas", async (ct, ctx) =>
            {
                var currentWorkspace = _workspaceService.Current;
                if (currentWorkspace?.EntireFrameData is null) return;

                var intensityMap = _imageProcessService.ComputeIntensityDataMap(
                    currentWorkspace.EntireFrameData,
                    currentWorkspace.RegionDatas.ToList(),
                    CurrentWorkingDistance);

                _workspaceService.Update(ws => ws.UpdateIntensityDataMap(intensityMap));
            });
        }

        private void RestartConsumeLoop()
        {
            CancelConsumeLoop();
            _stopRequested = false;
            _consumeCts = new CancellationTokenSource();
            _consumeTask = Task.Run(() => ConsumeFramesAsync(_consumeCts.Token));
        }

        private void CancelConsumeLoop() => _consumeCts?.Cancel();

        private async Task ConsumeFramesAsync(CancellationToken ct)
        {
            var reader = _cameraService.Frames;
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var frame))
                    {
                        if (_stopRequested) { frame.Dispose(); continue; }

                        _imageProcessService.ProcessFrame(frame);

                        var fullFrameClone = _imageProcessService.CloneFrameData(frame);
                        _workspaceService.SetEntireFrame(fullFrameClone);

                        var previewCrop = _imageProcessService.GetCropFrameData(frame, CurrentWavelengthIndex, CurrentWorkingDistance);
                        var oldPreview = Interlocked.Exchange(ref _previewFrameData, previewCrop);
                        oldPreview?.Dispose();

                        var rgbFrame = _imageProcessService.GetRgbFrameData(frame, CurrentWorkingDistance);
                        var oldRgb = Interlocked.Exchange(ref _rgbFrameData, rgbFrame);
                        oldRgb?.Dispose();

                        _uiThrottler.Run(UpdateUI);

                        if (Interlocked.CompareExchange(ref _isAnalyzing, 1, 0) == 0)
                        {
                            var analysisFrame = _imageProcessService.CloneFrameData(frame);
                            _ = Task.Run(() => RunAnalysisAndUnlock(analysisFrame));
                        }

                        frame.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine(ex); }
            finally { CleanupPendingFrames(); }
        }

        private void RunAnalysisAndUnlock(FrameData frame)
        {
            try
            {
                var regions = _workspaceService.Current?.RegionDatas?.ToList();

                // [핵심 수정] 빈 리스트(Count=0)여도 분석을 수행해야 함.
                // 그래야 '빈 맵'이 생성되어 차트를 클리어할 수 있음.
                if (regions != null)
                {
                    var map = _imageProcessService.ComputeIntensityDataMap(frame, regions, CurrentWorkingDistance);
                    _workspaceService.Update(ws => ws.UpdateIntensityDataMap(map));
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            finally
            {
                frame.Dispose();
                Interlocked.Exchange(ref _isAnalyzing, 0);
            }
        }

        private void CleanupPendingFrames()
        {
            var p = Interlocked.Exchange(ref _previewFrameData, null); p?.Dispose();
            var a = Interlocked.Exchange(ref _rgbFrameData, null); a?.Dispose();
            _uiThrottler.Reset();
            Interlocked.Exchange(ref _isAnalyzing, 0);
        }
        private void UpdateUI()
        {
            if (!IsPreviewing) return;
            UpdatePreviewUI();
            UpdateRGBUI();
        }
        private void UpdatePreviewUI()
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
                    PreviewInvalidated?.Invoke();
                }
            }
            finally { frame.Dispose(); }
        }
        private void UpdateRGBUI()
        {
            if (!IsPreviewing) return;
            var frame = Interlocked.Exchange(ref _rgbFrameData, null);
            if (frame is null) return;
            try
            {
                EnsureRgbBitmap(frame.Width, frame.Height);
                if (_rgbBitmap != null)
                {
                    _imageProcessService.ConvertRgbFrameDataToWriteableBitmap(_rgbBitmap, frame);
                    RgbPreviewInvalidated?.Invoke();
                }
            }
            finally { frame.Dispose(); }
        }

        private void DisplayWorkspaceImage(int index, int wd)
        {
            var ws = _workspaceService.Current;
            var frame = ws?.EntireFrameData;
            if (frame is null) return;

            var crop = _imageProcessService.GetCropFrameData(frame, index, wd);
            try
            {
                EnsureSharedPreview(crop.Width, crop.Height);
                if (_previewBitmap is not null)
                {
                    _imageProcessService.ConvertFrameDataToWriteableBitmap(_previewBitmap, crop);
                    PreviewInvalidated?.Invoke();
                }
            }
            finally { crop.Dispose(); }
        }

        private void EnsureSharedPreview(int width, int height)
        {
            if (_previewBitmap is not null)
            {
                var ps = _previewBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height) return;
                _previewBitmap.Dispose();
                _previewBitmap = null;
            }
            _previewBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Gray8, AlphaFormat.Opaque);
            PreviewBitmap = _previewBitmap;
        }
        private void EnsureRgbBitmap(int width, int height)
        {
            if (_rgbBitmap is not null)
            {
                var ps = _rgbBitmap.PixelSize;
                if (ps.Width == width && ps.Height == height) return;
                _rgbBitmap.Dispose();
                _rgbBitmap = null;
            }
            _rgbBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgr24, AlphaFormat.Opaque);
            RgbBitmap = _rgbBitmap;
        }

        public override async ValueTask DisposeAsync()
        {
            CancelConsumeLoop();
            if (_consumeTask != null) await _consumeTask.ConfigureAwait(false);
            CleanupPendingFrames();
            try { await _cameraService.StopPreviewAndDisconnectAsync(CancellationToken.None); } catch { }
            _previewBitmap?.Dispose();
            _rgbBitmap?.Dispose();
            await base.DisposeAsync();
        }
    }
}